using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

/*

    Smarken 2017

    Thanks to Manghao for making me mad

 */
namespace DofusKeyFinder
{
    public class Frequency
    {
        public Dictionary<char, float> Global;
        public Dictionary<char, float>[] Position;
    }


    /*
        Achieved 75% success
     */
    class Program
    {
        const int MinKeyLength = 128;
        const int MaxKeyLength = 300;

        public static void Main(String[] args)
        {
            var frequency = LoadFrequency();
            /*
            
                var guessedKey = ComputeKey(frequency, HexToString(map.EncodedData));
            
             */
        }

        public static string HEX_CHARS = "0123456789ABCDEF";
        public static string Checksum(String key)
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
            var k = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                var currentData = int.Parse(data.Substring(i, 2), NumberStyles.HexNumber);
                var currentKey = char.ConvertToUtf32(key, (k++ + shift) % keyLength);
                decrypted.Append(char.ConvertFromUtf32(currentData ^ currentKey));
            }
            return Uri.UnescapeDataString(decrypted.ToString());
        }

        public static string HexToString(string data)
        {
            var output = new StringBuilder(data.Length / 2);
            for (var i = 0; i < data.Length; i += 2)
            {
                output.Append(char.ConvertFromUtf32(int.Parse(data.Substring(i, 2), NumberStyles.HexNumber)));
            }
            return output.ToString();
        }

        public static string PrepareKey(string data)
        {
            return Uri.UnescapeDataString(HexToString(data));
        }

        public static HashSet<int> ComputeMaxShifts(string s)
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

        public static string LeftRotateShift(string key, int shift)
        {
            shift %= key.Length;
            return key.Substring(shift) + key.Substring(0, shift);
        }

        public static string RightRotateShift(string key, int shift)
        {
            shift %= key.Length;
            return key.Substring(key.Length - shift) + key.Substring(0, key.Length - shift);
        }

        private static int GCD(int a, int b)
        {
            return (a == 0 || b == 0) ? a | b : GCD(Math.Min(a, b), Math.Max(a, b) % Math.Min(a, b));
        }

        public static Frequency LoadFrequency()
        {
            return Newtonsoft.Json.JsonConvert.DeserializeAnonymousType<Frequency>(File.ReadAllText("./frequencies.json"), new Frequency());
        }

        public static string ComputeKey(Frequency frequency, string encodedData)
        {
            var shifts = ComputeMaxShifts(encodedData)
                .Where(shift => shift > MinKeyLength)
                .ToList();
            var computedKeyLength = shifts.Aggregate(GCD);
            if (computedKeyLength < MinKeyLength || computedKeyLength > MaxKeyLength)
                throw new InvalidOperationException("invalid key length computed");
            return GuessKey(encodedData, computedKeyLength, frequency);
        }

        private static string GuessKey(string data, int keyLength, Frequency frequency)
        {
            var blocks = new List<string>();
            for (var i = 0; i < data.Length - keyLength; i += keyLength)
            {
                blocks.Add(data.Substring(i, keyLength));
            }
            var key = new StringBuilder();
            for (var i = 0; i < keyLength; i++)
            {
                var cryptedBlock = new string(blocks.Select(block => block[i]).ToArray());
                var bestError = float.MaxValue;
                var bestXor = 0;
                for (var j = 0; j < 255; j++)
                {
                    var decryptedBlock = new StringBuilder(cryptedBlock.Length);
                    for (var k = 0; k < cryptedBlock.Length; k++)
                    {
                        decryptedBlock.Append(char.ConvertFromUtf32(cryptedBlock[k] ^ j));
                    }
                    var decrypted = Uri.UnescapeDataString(decryptedBlock.ToString());
                    var error = ComputeError(decrypted, i, frequency);
                    if (error <= bestError)
                    {
                        bestError = error;
                        bestXor = j;
                    }
                }
                key.Append((char)bestXor);
            }
            var stringKey = key.ToString();
            int shift = int.Parse(Checksum(stringKey), NumberStyles.HexNumber) * 2;
            return RightRotateShift(stringKey, shift);
        }
        private static float ComputeError(string decrypted, int position, Frequency frequency)
        {
            var currentFrequencies = new Dictionary<char, int>();
            PopulateFrequency(decrypted, currentFrequencies);

            var globalError = 0f;
            foreach (var kv in currentFrequencies)
            {
                if (!frequency.Global.ContainsKey(kv.Key))
                {
                    globalError += 10;
                }
                else
                {
                    var globalFreq = frequency.Global[kv.Key];
                    var currentFreq = kv.Value / (float)decrypted.Length;
                    globalError += Math.Abs(currentFreq - globalFreq);
                }
            }

            float positionBonus = 0;
            for (var i = 0; i < decrypted.Length; i++)
            {
                var key = decrypted[i];
                var positionFrequenciesI = frequency.Position[position % 10];
                if (positionFrequenciesI.ContainsKey(key))
                {
                    // we shoud be doing something 
                    // positionBonus -= (float)Math.Pow(positionFrequenciesI[key], 10);
                    positionBonus += positionFrequenciesI[key];
                }
            }
            return globalError; //+ (1 - (positionBonus / decrypted.Length));
        }

        private static void PopulateFrequency(string input, IDictionary<char, int> count)
        {
            foreach (var c in input)
            {
                if (count.ContainsKey(c))
                    count[c]++;
                else
                    count[c] = 1;
            }
        }
    }
}
