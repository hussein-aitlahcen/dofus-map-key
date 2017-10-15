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
        public string Key;
        public string DecodedData;
        public string EncodedData;
    }

    public class Frequencies
    {
        public Dictionary<char, float> Global;
        public Dictionary<char, float>[] Position;
    }

    public class ComputedKey
    {
        public string Value;
        public Dictionary<int, List<(int, double)>> Alternatives;
    }


    /*
        Achieved 75% success xd
     */
    class Program
    {
        const int ValidKeyAltThreshold = 15;
        const double ErrorClamp = 1.15;
        const int CellPatternSize = 10;
        const int MinKeyLength = 128;
        const int MaxKeyLength = 256;
        const int MinKeyXor = 32;
        const int MaxKeyXor = 127;
        public static string HEX_CHARS = "0123456789ABCDEF";
        public static string MapsPath = "../maps";
        public static string FrequenciesPath = "./frequencies.json";
        public static string InputPath = "./input";
        public static string OutputPath = "./output";

        public static IEnumerable<Map> LoadMaps()
        {
            return Directory.GetFiles(MapsPath, "*.json")
                .Select(file => JsonConvert.DeserializeAnonymousType<Map>(File.ReadAllText(file), new Map()));
        }

        public static Frequencies LoadFrequencies()
        {
            return JsonConvert.DeserializeAnonymousType<Frequencies>(File.ReadAllText(FrequenciesPath), new Frequencies());
        }

        public static void Main(String[] args)
        {
            var frequencies = LoadFrequencies();
            //Benchmark(frequencies);
            ExportCompute(frequencies);
        }

        public static void Benchmark(Frequencies frequencies)
        {
            var hit = 0;
            var tih = 0;
            var alt = 0;
            var bad = 0;
            foreach (var map in LoadMaps())
            {
                tih++;
                var realKey = PrepareKey(map.Key);
                var encodedData = HexToString(map.EncodedData);
                var keyLength = ComputeKeyLengthHamming(encodedData);
                var key = GuessKey(encodedData, keyLength, frequencies);
                if (key.Alternatives.Count > ValidKeyAltThreshold)
                {
                }
                if (key.Value == realKey)
                {
                    if (key.Alternatives.Count > 0)
                    {
                        Console.WriteLine($"ALT: {map.Id}, {key.Alternatives.Count}");
                    }
                    hit++;
                }
                else
                {
                    if (key.Alternatives.Count > 0)
                    {
                        bad++;
                        alt += key.Alternatives.Count;
                        Console.WriteLine(alt / (float)bad);
                    }
                    else
                    {
                        Console.WriteLine($"IMP: {map.Id}, {key.Alternatives.Count}");
                    }
                }
            }
        }

        public static List<string> ComputeAlternatives(ComputedKey key)
        {
            var alternatives = new List<string>();
            var maxOffset = key.Alternatives.Max(kv => kv.Value.Count);
            for (var i = 1; i < maxOffset; i++)
            {
                var altKey = key.Value.ToArray();
                foreach (var kv in key.Alternatives)
                {
                    var positionAlt = kv.Value;
                    var altOffset = positionAlt.Count - 1 - i;
                    if (altOffset < 0)
                        altOffset = positionAlt.Count - 1;
                    var (c, score) = positionAlt[altOffset];
                    altKey[kv.Key] = (char)c;
                }
                alternatives.Add(new string(altKey));
            }
            return alternatives;
        }

        public static void ExportCompute(Frequencies frequencies)
        {
            Directory.CreateDirectory(InputPath);
            Directory.CreateDirectory(OutputPath);
            Directory.GetFiles(InputPath, "*")
                .ToObservable()
                .Select(path => new { EncodedData = HexToString(File.ReadAllText(path)), Name = Path.GetFileName(path) })
                .Do(x => Console.WriteLine("processing: " + x.Name))
                .Select(x => new { Header = x, KeyLength = ComputeKeyLengthHamming(x.EncodedData) })
                .Select(y => new { Data = y, Key = GuessKey(y.Header.EncodedData, y.KeyLength, frequencies) })
                .Do(z => Console.WriteLine($"alternatives: {z.Key.Alternatives.Count}"))
                .Do(z => File.WriteAllText(string.Format("{0}/{1}", OutputPath, z.Data.Header.Name + "_key.txt"), FormatKeyExport(z.Key.Value)))
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(z =>
                {
                    Console.WriteLine("done.");
                });
        }

        public static string Checksum(string key)
        {
            int checksum = 0;
            for (int i = 0; i < key.Length; i++)
            {
                checksum += char.ConvertToUtf32(key, i) % 16;
            }
            return char.ToString(HEX_CHARS[checksum % 16]);
        }

        public static string Decrypt(string data, string key)
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

        public static string HexToString(string data)
        {
            var output = new StringBuilder(data.Length / 2);
            for (var i = 0; i < data.Length; i += 2)
            {
                output.Append((char)int.Parse(data.Substring(i, 2), NumberStyles.HexNumber));
            }
            return output.ToString();
        }

        public static string PrepareKey(string data)
        {
            return HttpUtility.UrlDecode(HexToString(data));
        }

        public static string PreEscape(string input)
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

        public static string FormatKeyExport(string key)
        {
            return PreEscape(key).Select(c => String.Format("{0:X}", (int)c)).Aggregate("", (acc, c) => acc + c);
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

        public static int ComputeKeyLengthKasysky(string encodedData)
        {
            return ComputeMaxShifts(encodedData)
                .Where(shift => shift >= MinKeyLength)
                .Aggregate(GCD);
        }

        public static int ComputeHammingDistance(string message, int x, int y, int keyLength)
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
        public static int ComputeKeyLengthHamming(string encodedData)
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

        private static ComputedKey GuessKey(string message, int keyLength, Frequencies frequencies)
        {
            var alternatives = new Dictionary<int, List<(int, double)>>();
            var blockSize = keyLength;
            var numberOfBlock = message.Length / blockSize;
            var key = new StringBuilder();
            for (var blockOffset = 0; blockOffset < blockSize; blockOffset++)
            {
                alternatives.Add(blockOffset, new List<(int, double)>());
                var bestError = double.MaxValue;
                var bestXor = -1;
                // Only between thoses
                for (var xorKey = MinKeyXor; xorKey <= MaxKeyXor; xorKey++)
                {
                    var decryptedBlock = new char[numberOfBlock];
                    for (var blockNumber = 0; blockNumber < numberOfBlock; blockNumber++)
                    {
                        var currentData = message[blockNumber * blockSize + blockOffset];
                        decryptedBlock[blockNumber] = (char)(currentData ^ xorKey);
                    }
                    var decrypted = HttpUtility.UrlDecode(new string(decryptedBlock));
                    var error = ComputeError(decrypted, blockOffset, blockSize, frequencies);
                    if (error <= bestError)
                    {
                        if (error == bestError)
                        {
                            if (bestXor != -1)
                            {
                                alternatives[blockOffset].Add((xorKey, error));
                            }
                        }
                        else
                        {
                            var clampedValue = error * ErrorClamp;
                            alternatives[blockOffset].RemoveAll(x => x.Item2 > clampedValue);
                            alternatives[blockOffset].Add((xorKey, error));
                        }
                        bestError = error;
                        bestXor = xorKey;
                    }
                }
                key.Append((char)bestXor);
            }
            // Shift back the key with its checksum
            var computedKey = key.ToString();
            int shift = int.Parse(Checksum(computedKey), NumberStyles.HexNumber) * 2;
            var finalKey = RightRotateShift(computedKey, shift);
            return new ComputedKey
            {
                Value = finalKey,
                Alternatives = alternatives.Where(x => x.Value.Count > 1).ToDictionary(x => x.Key, x => x.Value)
            };
        }

        private static double ComputeError(string decrypted, int blockOffset, int blockSize, Frequencies frequencies)
        {
            var currentFrequencies = GetFrequencies(decrypted);
            return GetPositionError(decrypted, blockOffset, blockSize, frequencies);
        }

        public static double GetPositionError(string decrypted, int blockOffset, int blockSize, Frequencies frequencies)
        {
            var distance = 0.0;
            for (var i = 0; i < decrypted.Length; i++)
            {
                var absolutePosition = blockSize * i + blockOffset;
                var positionFrequencies = frequencies.Position[absolutePosition % CellPatternSize];
                var currentData = decrypted[i];
                if (positionFrequencies.ContainsKey(currentData))
                {
                    distance += (1 - positionFrequencies[currentData]);
                }
                else
                {
                    distance += 10;
                }
            }
            return distance;
        }

        // Squared Euclidean distance
        public static double GetDistance(Dictionary<char, float> u, Dictionary<char, float> v)
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

        private static Dictionary<char, float> GetFrequencies(string input)
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
