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
        const int beatsInMeasure = 4; // CM works in "beats" while Simai works in measures

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
                if (notesToScan.Count == 0)
                {
                    stringBuilder.AppendLine("{16},,,,");
                }
                else
                {
                    var fractions = notesToScan.Select(baseNote => (baseNote, RealToFraction(baseNote.JsonTime, 0.001))).ToList();
                    
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
                            switch (baseNote.PosX)
                            {
                                case 0:
                                    if (baseNote.PosY == 0) stringBuilder.Append("6");
                                    if (baseNote.PosY is 1 or 2) stringBuilder.Append("7");
                                    break;
                                case 1:
                                    if (baseNote.PosY == 0) stringBuilder.Append("5");
                                    if (baseNote.PosY is 1 or 2) stringBuilder.Append("8");
                                    break;
                                case 2:
                                    if (baseNote.PosY == 0) stringBuilder.Append("4");
                                    if (baseNote.PosY is 1 or 2) stringBuilder.Append("1");
                                    break;
                                case 3:
                                    if (baseNote.PosY == 0) stringBuilder.Append("3");
                                    if (baseNote.PosY is 1 or 2) stringBuilder.Append("2");
                                    break;
                            }
                            
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

                            var attachedArc = difficulty.Arcs.Find(arc => Mathf.Approximately(arc.JsonTime, baseNote.JsonTime) && arc.PosX == baseNote.PosX && arc.PosY == baseNote.PosY);
                            if (attachedArc != null)
                            {
                                var arcFraction = RealToFraction(attachedArc.TailJsonTime - attachedArc.JsonTime, 0.001);
                                stringBuilder.Append($"h[{arcFraction.D * beatsInMeasure}:{arcFraction.N}]");
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

            var data = Regex.Replace(notatedNotes, @"\s+", "");

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

                                if (note.Contains('h'))
                                {
                                    var regex = new Regex(@"\[(\d+):(\d+)\]");
                                    var matches = regex.Match(note);
                                    var numerator = int.Parse(matches.Groups[1].Value);
                                    var denominator = int.Parse(matches.Groups[2].Value);
                                    var secondsInBeat = 60 / bpm;
                                    var holdTime = secondsInBeat / (numerator / 4.0) * denominator;
                                    noteObject["holdTime"] = holdTime;
                                }
                                else
                                {
                                    noteObject["holdTime"] = 0.0;
                                }
                                
                                

                                noteObject["isBreak"] = note.Contains('b');
                                noteObject["isEx"] = note.Contains('x');
                                noteObject["isFakeRotate"] =  false;
                                noteObject["isForceStar"] =  false;
                                noteObject["isHanabi"] =  false;
                                noteObject["isSlideBreak"] =  false;
                                noteObject["isSlideNoHead"] =  false;
                                noteObject["noteContent"] = note;
                                noteObject["noteType"] =  note.Contains('h') ? 2 : 0;
                                noteObject["slideStartTime"] =  0.0;
                                noteObject["slideTime"] =  0.0;
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
                N = n % d;
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
