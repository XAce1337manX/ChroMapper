using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Beatmap.Enums;
using SimpleJSON;
using UnityEngine;

namespace Beatmap.Base
{
    public static class Maimai
    {
        private const int beatsInMeasure = 4; // CM works in "beats" while Simai works in measures
        private const double accuracy = 0.001;
        
        // Simai format
        public static string DoTheThing(BaseDifficulty difficulty)
        {
            var stringBuilder = new StringBuilder();

            var songInfo = BeatSaberSongContainer.Instance.Info;

            stringBuilder.AppendLine($"&title={songInfo.SongName}");
            stringBuilder.AppendLine($"&artist={songInfo.SongAuthorName}");
            stringBuilder.AppendLine($"&first=0");
            stringBuilder.AppendLine($"&des={songInfo.LevelAuthorName}");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"&lv_5=13");
            stringBuilder.AppendLine($"&inote_5=");

            var notatedNotes = GetNotatedNotes(difficulty);
            stringBuilder.AppendLine(notatedNotes);
            
            return stringBuilder.ToString();
        }

        private static string GetNotatedNotes(BaseDifficulty difficulty)
        {
            var bpm = BeatSaberSongContainer.Instance.Info.BeatsPerMinute;
            
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"({bpm})");
            
            var beat = 0;

            var notesToScan = difficulty.Notes.Where(x => beat <= x.JsonTime && x.JsonTime < beat + 1).ToList();
            while (notesToScan.Count != 0 || difficulty.Notes.Any(x => x.JsonTime > beat))
            {
                var bpmEvent = difficulty.BpmEvents.Find(x => Mathf.Approximately(x.JsonTime, beat));
                if (bpmEvent != null)
                {
                    stringBuilder.AppendLine($"({bpmEvent.Bpm})");
                }
                
                var bookmark = difficulty.Bookmarks.Find(x => Mathf.Approximately(x.JsonTime, beat));
                if (bookmark != null)
                {
                    stringBuilder.AppendLine($"|| {bookmark.Name}");
                }
                
                if (notesToScan.Count == 0)
                {
                    stringBuilder.AppendLine("{16},,,,");
                }
                else
                {
                    var fractions = notesToScan.Select(baseNote => (baseNote, RealToFraction(baseNote.JsonTime, accuracy))).ToList();
                    foreach (var fraction in fractions)
                    {
                        fraction.Item2.N %= fraction.Item2.D;
                    }
                    
                    var lcm = LCM(fractions.Select(f => f.Item2.D)) * beatsInMeasure;

                    foreach (var fraction in fractions)
                    {
                        fraction.Item2.N *= (lcm / fraction.Item2.D) / beatsInMeasure;
                    }

                    stringBuilder.Append($"{{{lcm}}}");

                    var commaCounter = 0;
                    while (commaCounter < lcm / beatsInMeasure)
                    {
                        var notesForThisComma = fractions.Where(x => x.Item2.N == commaCounter).ToList();
                        var index = 0;
                        foreach (var thingy in notesForThisComma)
                        {
                            var baseNote = thingy.baseNote;
                            // Note position
                            var position = GetPosition(baseNote);
                            stringBuilder.Append($"{position}");
                            
                            // Red is break note
                            if (baseNote.Color == (int)NoteColor.Red)
                            {
                                stringBuilder.Append("b");
                            }
                            
                            // Dot is ex note
                            if (baseNote.CutDirection == (int)NoteCutDirection.Any)
                            {
                                stringBuilder.Append("x");
                            }

                            // Attached arcs are holds
                            var attachedArc = difficulty.Arcs.Find(arc => Mathf.Approximately(arc.JsonTime, baseNote.JsonTime) && arc.PosX == baseNote.PosX && arc.PosY == baseNote.PosY);
                            if (attachedArc != null)
                            {
                                var arcFraction = RealToFraction(attachedArc.TailJsonTime - attachedArc.JsonTime, accuracy);
                                stringBuilder.Append($"h[{arcFraction.D * beatsInMeasure}:{arcFraction.N}]");
                            }
                            
                            
                            // Attached chains to attached arcs are slides
                            var attachedChain = difficulty.Chains.Find(chain => Mathf.Approximately(chain.JsonTime, baseNote.JsonTime) && chain.PosX == baseNote.PosX && chain.PosY == baseNote.PosY);
                            if (attachedChain != null)
                            {
                                var slideArc = difficulty.Arcs.Find(arc => Mathf.Approximately(arc.JsonTime, attachedChain.TailJsonTime) && arc.PosX == attachedChain.PosX && arc.PosY == attachedChain.PosY);
                                if (slideArc != null)
                                {
                                    var tailPosition = GetTailPosition(slideArc);
                                    if (slideArc.HeadControlPointLengthMultiplier == 0f)
                                    {
                                        stringBuilder.Append($"-{tailPosition}");
                                    }
                                    else
                                    {
                                        if (slideArc.CutDirection is (int)NoteCutDirection.Right
                                            or (int)NoteCutDirection.UpRight or (int)NoteCutDirection.DownRight)
                                        {
                                            stringBuilder.Append($">{tailPosition}");
                                        }
                                        else
                                        {
                                            stringBuilder.Append($"<{tailPosition}");
                                        }
                                    }
                                    
                                    // What a pain. Calculate the bpm and divisions needed.
                                    stringBuilder.Append('[');
                                    
                                    var bpmFraction = RealToFraction(attachedChain.TailJsonTime - attachedChain.JsonTime, accuracy);
                                    var slideDelayBpm = bpm * bpmFraction.D / bpmFraction.N;

                                    var slideFraction = RealToFraction(slideArc.TailJsonTime - slideArc.JsonTime, accuracy);
                                    
                                    var x = slideFraction.D * beatsInMeasure;
                                    var y = slideFraction.N;

                                    var bpmScaling = slideDelayBpm / bpm;

                                    var yScaled = y * bpmScaling;
                                    var yScaledDecimal = yScaled - (int)yScaled;
                                    if (yScaledDecimal > 0.001f) 
                                    {
                                        // Simai doesn't support floats in here so scale it to int
                                        var correctionFraction = RealToFraction(yScaledDecimal, accuracy);
                                        x *= correctionFraction.D;
                                        y = Mathf.RoundToInt(y * bpmScaling * correctionFraction.D);
                                    }
                                    else
                                    {
                                        y = Mathf.RoundToInt(y * bpmScaling);
                                    }
                                    
                                    stringBuilder.Append($"{slideDelayBpm}#{x}:{y}]");
                                }
                            }
                            
                            index++;
                            if (index < notesForThisComma.Count)
                            {
                                stringBuilder.Append("/");
                            }
                        }

                        stringBuilder.Append(',');
                        commaCounter++;
                    }

                    stringBuilder.AppendLine();
                }

                beat++;
                notesToScan = difficulty.Notes.Where(x => beat <= x.JsonTime && x.JsonTime < beat + 1).ToList();
            }
            
            stringBuilder.AppendLine("E");

            return stringBuilder.ToString();
        }

        private static int GetPosition(BaseGrid baseGrid)
        {
            switch (baseGrid.PosX)
            {
                case 0:
                    if (baseGrid.PosY is 0 or 1) return 6;
                    if (baseGrid.PosY is 3 or 4) return 7;
                    break;
                case 1:
                    if (baseGrid.PosY is 0 or 1) return 5;
                    if (baseGrid.PosY is 3 or 4) return 8;
                    break;
                case 2:
                    if (baseGrid.PosY is 0 or 1) return 4;
                    if (baseGrid.PosY is 3 or 4) return 1;
                    break;
                case 3:
                    if (baseGrid.PosY is 0 or 1) return 3;
                    if (baseGrid.PosY is 3 or 4) return 2;
                    break;
            }

            return 1;
        }
        
        private static int GetTailPosition(BaseSlider baseSlider)
        {
            switch (baseSlider.TailPosX)
            {
                case 0:
                    if (baseSlider.TailPosY is 0 or 1) return 6;
                    if (baseSlider.TailPosY is 3 or 4) return 7;
                    break;
                case 1:
                    if (baseSlider.TailPosY is 0 or 1) return 5;
                    if (baseSlider.TailPosY is 3 or 4) return 8;
                    break;
                case 2:
                    if (baseSlider.TailPosY is 0 or 1) return 4;
                    if (baseSlider.TailPosY is 3 or 4) return 1;
                    break;
                case 3:
                    if (baseSlider.TailPosY is 0 or 1) return 3;
                    if (baseSlider.TailPosY is 3 or 4) return 2;
                    break;
            }

            return 1;
        }
        
        // Format for the viewer
        public static string DoTheOtherThing(BaseDifficulty difficulty)
        {
            var json = new JSONObject();
                
            var songInfo = BeatSaberSongContainer.Instance.Info;

            json["title"]= songInfo.SongName;
            json["artist"] = songInfo.SongAuthorName;
            json["designer"] = songInfo.LevelAuthorName;
            json["difficulty"] = "MASTER";
            json["diffNum"] = 4; // I guess this is basically difficultyRank
            json["level"] = 13;

            var timingList = new JSONArray();

            var notatedNotes = GetNotatedNotes(difficulty);

            // Yeet comments
            var data = Regex.Replace(notatedNotes, $@"\|\| .*{Environment.NewLine}", "");
            
            // Yeet whitespace
            data = Regex.Replace(data, @"\s+", "");

            var i = 0;
            var realTime = 0.0;
            var measureDivision = 16;
            var bpm = (double)songInfo.BeatsPerMinute;
            while (i < data.Length)
            {
                if (data[i] == 'E') break;

                switch (data[i])
                {
                    // BPM point
                    case '(':
                        {
                            var j = i + 1;
                            while (data[j] != ')') j++;
                    
                            bpm = float.Parse(data.Substring(i + 1, j - i - 1));

                            i = j + 1;
                            break;
                        }
                    // Measure point
                    case '{':
                        {
                            var j = i + 1;
                            while (data[j] != '}') j++;
                    
                            measureDivision = int.Parse(data.Substring(i + 1, j - i - 1));

                            i = j + 1;
                            break;
                        }
                    case ',':
                        {
                            var secondsInBeat = 60 / bpm;
                            realTime += secondsInBeat / (measureDivision / 4.0);
                            i++;
                            break;
                        }
                    default:
                        {
                            // Here we go. An actual note thing
                            var timingObject = new JSONObject();
                            timingObject["currentBpm"] = bpm;
                            timingObject["havePlayed"] = false;
                            timingObject["HSpeed"] = 1.0;
                    
                            var j = i + 1;
                            while (data[j] != ',') j++;
                    
                            var notesContent = data.Substring(i, j - i);

                            timingObject["notesContent"] = notesContent;
                            timingObject["rawTextPositionX"] = 1;
                            timingObject["rawTextPositionY"] = 1;
                            timingObject["time"] = realTime;
                    
                            var noteList = new JSONArray();
                            var noteString = notesContent.Split('/');
                            foreach (var note in noteString)
                            {
                                var noteObject = new JSONObject();

                                noteObject["noteType"] = 0; // Default to tap
                                
                                
                                // Is Hold
                                if (note.Contains('h'))
                                {
                                    var regex = new Regex(@"\[(\d+):(\d+)\]");
                                    var matches = regex.Match(note);
                                    var numerator = int.Parse(matches.Groups[1].Value);
                                    var denominator = int.Parse(matches.Groups[2].Value);
                                    var secondsInBeat = 60 / bpm;
                                    var holdTime = secondsInBeat / (numerator / 4.0) * denominator;
                                    noteObject["holdTime"] = holdTime;
                                    noteObject["noteType"] = 2;
                                }
                                else
                                {
                                    noteObject["holdTime"] = 0.0;
                                }
                                
                                // Is Arc
                                if (note.Contains('-') || note.Contains('<') || note.Contains('>'))
                                {
                                    // Matches are:
                                    // * Slide start position
                                    // * Slide type
                                    // * Slide end position
                                    // * Slide bpm
                                    // * Slide numerator
                                    // * Slide denominator
                                    // e.g. 5>2[86.66666#12:2]
                                    var regex = new Regex(@"(\d)([-<>])(\d)\[(\d+\.?\d*)#(\d+)\:(\d+)\]");
                                    
                                    var matches = regex.Match(note);
                                    var slideBpm = double.Parse(matches.Groups[4].Value);
                                    var numerator = int.Parse(matches.Groups[5].Value);
                                    var denominator = int.Parse(matches.Groups[6].Value);
                                    var secondsInSlideBeat = 60 / slideBpm;
                                    var slideStartTime = realTime + secondsInSlideBeat;
                                    var slideTime = secondsInSlideBeat / (numerator / 4.0) * denominator;
                                    
                                    noteObject["noteType"] = 1;
                                    noteObject["slideStartTime"] = slideStartTime;
                                    noteObject["slideTime"] =  slideTime;
                                }
                                else
                                {
                                    noteObject["slideStartTime"] =  0.0;
                                    noteObject["slideTime"] =  0.0;
                                }
                                
                                

                                noteObject["isBreak"] = note.Contains('b');
                                noteObject["isEx"] = note.Contains('x');
                                noteObject["isFakeRotate"] =  false;
                                noteObject["isForceStar"] =  false;
                                noteObject["isHanabi"] =  false;
                                noteObject["isSlideBreak"] =  false;
                                noteObject["isSlideNoHead"] =  false;
                                noteObject["noteContent"] = note;
                                noteObject["startPosition"] =  note[0].ToString();
                                noteObject["touchArea"] =  " ";

                                noteList.Add(noteObject);
                            }
                    
                            timingObject["noteList"] = noteList;
                            timingList.Add(timingObject);
                    
                            i = j;
                            break;
                        }
                }
            }
            
            json["timingList"] = timingList;
            
            return json.ToString(2);
        }
        
        // Yoinked from StackExchange
        private static int LCM(IEnumerable<int> numbers)
        {
            return numbers.Aggregate(lcm);
        }

        private static int lcm(int a, int b)
        {
            return Math.Abs(a * b) / GCD(a, b);
        }

        private static int GCD(int a, int b)
        {
            return b == 0 ? a : GCD(b, a % b);
        }
        
        // Yoinked from AutoModder
        public class Fraction
        {
            public int N;
            public int D;

            public Fraction(int n, int d)
            {
                N = n;
                D = d;
            }
        }
        
        private static Fraction RealToFraction(double value, double accuracy)
        {
            if (accuracy <= 0.0 || accuracy >= 1.0)
            {
                throw new ArgumentOutOfRangeException("accuracy", "Must be > 0 and < 1.");
            }

            int sign = Math.Sign(value);

            if (sign == -1)
            {
                value = Math.Abs(value);
            }

            // Accuracy is the maximum relative error; convert to absolute maxError
            double maxError = sign == 0 ? accuracy : value * accuracy;

            int n = (int)Math.Floor(value);
            value -= n;

            if (value < maxError)
            {
                return new Fraction(sign * n, 1);
            }

            if (1 - maxError < value)
            {
                return new Fraction(sign * (n + 1), 1);
            }

            // The lower fraction is 0/1
            int lower_n = 0;
            int lower_d = 1;

            // The upper fraction is 1/1
            int upper_n = 1;
            int upper_d = 1;

            while (true)
            {
                // The middle fraction is (lower_n + upper_n) / (lower_d + upper_d)
                int middle_n = lower_n + upper_n;
                int middle_d = lower_d + upper_d;

                if (middle_d * (value + maxError) < middle_n)
                {
                    // real + error < middle : middle is our new upper
                    upper_n = middle_n;
                    upper_d = middle_d;
                }
                else if (middle_n < (value - maxError) * middle_d)
                {
                    // middle < real - error : middle is our new lower
                    lower_n = middle_n;
                    lower_d = middle_d;
                }
                else
                {
                    // Middle is our best fraction
                    return new Fraction((n * middle_d + middle_n) * sign, middle_d);
                }
            }
        }
    }
}
