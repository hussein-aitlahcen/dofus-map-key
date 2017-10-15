using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using Newtonsoft.Json;

/*

    Smarken 2017

    Thanks to Manghao for making me mad

 */
namespace DofusKeyFinder
{
    public class Map
    {
        public int Id;
        public int Width;
        public int Height;
        public String Key;
        public String DecodedData;
        public String EncodedData;
    }

    public class Frequencies
    {
        public Dictionary<char, float> Global;
        public Dictionary<char, float>[] Position;
    }


    /*
        Achieved 75% success xd
     */
    class Program
    {
        const int MinKeyLength = 128;
        const int MaxKeyLength = 256;
        const int MinKeyXor = 32;
        const int MaxKeyXor = 127;
        public static String HEX_CHARS = "0123456789ABCDEF";
        public static String FrequenciesPath = "./frequencies.json";
        public static string InputPath = "./input";
        public static string OutputPath = "./output";

        public static Frequencies LoadFrequency()
        {
            return JsonConvert.DeserializeAnonymousType<Frequencies>(File.ReadAllText(FrequenciesPath), new Frequencies());
        }
        public static void Main(String[] args)
        {
            var frequency = LoadFrequency();
            Directory.CreateDirectory(InputPath);
            Directory.CreateDirectory(OutputPath);
            Directory.GetFiles(InputPath, "*")
                .ToObservable()
                .Select(path => new { EncodedData = HexToString(File.ReadAllText(path)), Name = Path.GetFileName(path) })
                .Do(x => Console.WriteLine("processing: " + x.Name))
                .Select(x => new { Header = x, KeyLength = ComputeKeyLengthHamming(x.EncodedData) })
                .Select(y => new { Data = y, Key = FormatKeyExport(GuessKey(y.Header.EncodedData, y.KeyLength, frequency)) })
                .Do(z => File.WriteAllText(string.Format("{0}/{1}", OutputPath, z.Data.Header.Name + "_key.txt"), z.Key))
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(z =>
                {
                    Console.WriteLine("done.");
                });
        }

        public static String Checksum(String key)
        {
            int checksum = 0;
            for (int i = 0; i < key.Length; i++)
            {
                checksum += char.ConvertToUtf32(key, i) % 16;
            }
            return char.ToString(HEX_CHARS[checksum % 16]);
        }
        public static String Decrypt(String data, String key)
        {
            int shift = int.Parse(Checksum(key), NumberStyles.HexNumber) * 2;
            var decrypted = new StringBuilder(data.Length);
            int keyLength = key.Length;
            for (var i = 0; i < data.Length; i++)
            {
                var currentData = data[i];
                var currentKey = key[(i + shift) % keyLength];
                decrypted.Append((char)(currentData ^ currentKey));
            }
            return HttpUtility.UrlDecode(decrypted.ToString());
        }

        public static String HexToString(String data)
        {
            var output = new StringBuilder(data.Length / 2);
            for (var i = 0; i < data.Length; i += 2)
            {
                output.Append((char)int.Parse(data.Substring(i, 2), NumberStyles.HexNumber));
            }
            return output.ToString();
        }

        public static String PrepareKey(String data)
        {
            return HttpUtility.UrlDecode(HexToString(data));
        }

        public static String PreEscape(String input)
        {
            var output = new StringBuilder();
            foreach (var c in input)
            {
                if (c < 32 || c > 127 || c == '%' || c == '+')
                {
                    output.Append(HttpUtility.UrlEncode(char.ToString(c)));
                }
                else
                {
                    output.Append(c);
                }
            }
            return output.ToString();
        }

        public static String FormatKeyExport(string key)
        {
            return PreEscape(key).Select(c => String.Format("{0:X}", (int)c)).Aggregate("", (acc, c) => acc + c);
        }

        public static HashSet<int> ComputeMaxShifts(String s)
        {
            var shifts = new HashSet<int>();
            int matchPos = 0, maxLength = 0;
            for (int shift = 1; shift < s.Length; shift++)
            {
                int matchCount = 0;
                for (int i = 0; i < s.Length - shift; i++)
                {
                    if (s[i] == s[i + shift])
                    {
                        matchCount++;
                        if (matchCount > maxLength)
                        {
                            maxLength = matchCount;
                            matchPos = i - matchCount + 1;
                            shifts.Add(shift);
                        }
                    }
                    else matchCount = 0;
                }
            }
            return shifts;
        }

        public static String LeftRotateShift(String key, int shift)
        {
            shift %= key.Length;
            return key.Substring(shift) + key.Substring(0, shift);
        }

        public static String RightRotateShift(String key, int shift)
        {
            shift %= key.Length;
            return key.Substring(key.Length - shift) + key.Substring(0, key.Length - shift);
        }

        private static int GCD(int a, int b)
        {
            return (a == 0 || b == 0) ? a | b : GCD(Math.Min(a, b), Math.Max(a, b) % Math.Min(a, b));
        }

        public static int ComputeKeyLengthKasysky(String encodedData)
        {
            return ComputeMaxShifts(encodedData)
                .Where(shift => shift >= MinKeyLength)
                .Aggregate(GCD);
        }

        public static int ComputeHammingDistance(String message, int x, int y, int keyLength)
        {
            var distance = 0;
            for (var i = 0; i < keyLength; i++)
            {
                var cA = message[x + i];
                var cB = message[y + i];
                var xor = cA ^ cB;
                while (xor != 0)
                {
                    distance++;
                    xor &= xor - 1;
                }
            }
            return distance;
        }

        /*

            100% Accurate

         */
        public static int ComputeKeyLengthHamming(String encodedData)
        {
            var bestKeyLength = 0;
            var bestScore = int.MaxValue;
            for (var keyLength = MinKeyLength; keyLength <= MaxKeyLength; keyLength++)
            {
                var numberOfBlock = encodedData.Length / keyLength;
                var currentScore = 0;
                for (var j = 0; j < numberOfBlock - 1; j++)
                {
                    for (var k = j + 1; k < numberOfBlock; k++)
                    {
                        var x = j * keyLength;
                        var y = k * keyLength;
                        currentScore += ComputeHammingDistance(encodedData, x, y, keyLength);
                    }
                }
                currentScore = currentScore / numberOfBlock;
                if (currentScore < bestScore)
                {
                    bestKeyLength = keyLength;
                    bestScore = currentScore;
                }
            }
            return bestKeyLength;
        }

        private static String GuessKey(String message, int keyLength, Frequencies frequency)
        {
            var numberOfBlock = message.Length / keyLength;
            var key = new StringBuilder();
            for (var indexInBlock = 0; indexInBlock < keyLength; indexInBlock++)
            {
                var bestError = double.MaxValue;
                var bestXor = 0;
                // Only between thoses
                for (var j = MinKeyXor; j <= MaxKeyXor; j++)
                {
                    var decryptedBlock = new char[numberOfBlock];
                    for (var k = 0; k < numberOfBlock; k++)
                    {
                        var currentData = message[k * keyLength + indexInBlock];
                        var currentKey = j;
                        decryptedBlock[k] = (char)(currentData ^ currentKey);
                    }
                    var decrypted = HttpUtility.UrlDecode(new String(decryptedBlock));
                    var error = ComputeError(decrypted, indexInBlock, frequency);
                    if (error <= bestError)
                    {
                        bestError = error;
                        bestXor = j;
                    }
                }
                key.Append((char)bestXor);
            }
            // Shift back the key with its checksum
            var StringKey = key.ToString();
            int shift = int.Parse(Checksum(StringKey), NumberStyles.HexNumber) * 2;
            return RightRotateShift(StringKey, shift);
        }

        private static double ComputeError(String decrypted, int indexInBlock, Frequencies frequency)
        {
            var currentFrequencies = GetFrequencies(decrypted);
            /*
                I don't know how to use that position frequency, seems to be 
                hard, even if the blocks are essentially based on the same
                characters
             */
            var positionFrequencies = frequency.Position[indexInBlock % 10];
            return GetFrequencyDistance(currentFrequencies, frequency.Global);
        }

        // Squared Euclidean distance
        public static double GetFrequencyDistance(Dictionary<char, float> u, Dictionary<char, float> v)
        {
            var distance = 0.0;
            foreach (var kv in u)
            {
                if (v.ContainsKey(kv.Key))
                {
                    distance += Math.Abs(kv.Value - v[kv.Key]);
                }
                else
                {
                    distance += 10;
                }
            }
            return distance;
        }

        private static Dictionary<char, float> GetFrequencies(String input)
        {
            var frequencies = new Dictionary<char, int>();
            foreach (var c in input)
            {
                if (frequencies.ContainsKey(c))
                    frequencies[c]++;
                else
                    frequencies[c] = 1;
            }
            return frequencies.ToDictionary(kv => kv.Key, kv => (float)(kv.Value / (float)input.Length));
        }
    }
}
