using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Beatmap.Base
{
    public static class Maimai
    {
        const int beatsInMeasure = 4; // CM works in "beats" while Simai works in measures

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
            stringBuilder.AppendLine($"&inote_5=({songInfo.BeatsPerMinute})");

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
                                    if (baseNote.PosY == 0) stringBuilder.Append("7");
                                    if (baseNote.PosY is 1 or 2) stringBuilder.Append("6");
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
            
            return stringBuilder.ToString();;
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
