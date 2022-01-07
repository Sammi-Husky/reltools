﻿using BrawlLib.Internal;
using System.Diagnostics;
using System.Text;

namespace System.IO
{
    public unsafe static class Util
    {
        //==================================================\\
        // Uses code from PSA (Project Smash Attacks) SSBB  \\
        // Moveset editor. Credit to PhantomWings and any   \\
        // others who contributed to the source code        \\
        //==================================================\\


        /// <summary>
        /// Retrieves a word from an array of bytes.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="endian"></param>
        /// <returns></returns>
        public static long GetWord(byte[] data, long offset, Endianness endian)
        {
            if (offset % 4 != 0) throw new Exception("Odd word offset.");
            if (offset >= data.Length) throw new Exception("Offset outside of expected value range.");

            if (endian == Endianness.Little)
            {
                return (uint)(data[offset + 3] * 0x1000000)
                     + (uint)(data[offset + 2] * 0x10000)
                     + (uint)(data[offset + 1] * 0x100)
                     + (uint)(data[offset + 0] * 0x1);
            }
            else
            {
                return (uint)(data[offset + 0] * 0x1000000)
                     + (uint)(data[offset + 1] * 0x10000)
                     + (uint)(data[offset + 2] * 0x100)
                     + (uint)(data[offset + 3] * 0x1);
            }
        }

        /// <summary>
        /// Retrieves an 32 bit integer from the specified address.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="endian"></param>
        /// <returns></returns>
        public static int GetWordUnsafe(VoidPtr address, Endianness endian)
        {
            if (address % 4 != 0)
                return 0;

            if (endian == Endianness.Big)
                return *(bint*)address;
            else
                return *(int*)address;
        }

        /// <summary>
        /// Sets a value into an array of bytes, resizing if necessary.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="value"></param>
        /// <param name="offset"></param>
        /// <param name="endian"></param>
        public static void SetWord(ref byte[] data, long value, long offset, Endianness endian)
        {
            if (offset % 4 != 0) throw new Exception("Odd word offset");
            if (offset >= data.Length)
            {
                Array.Resize<byte>(ref data, (int)offset + 4);
            }

            if (endian == Endianness.Big)
            {
                data[offset + 0] = (byte)((value & 0xFF000000) / 0x1000000);
                data[offset + 1] = (byte)((value & 0xFF0000) / 0x10000);
                data[offset + 2] = (byte)((value & 0xFF00) / 0x100);
                data[offset + 3] = (byte)((value & 0xFF) / 0x1);
            }
            else if (endian == Endianness.Little)
            {
                data[offset + 3] = (byte)((value & 0xFF000000) / 0x1000000);
                data[offset + 2] = (byte)((value & 0xFF0000) / 0x10000);
                data[offset + 1] = (byte)((value & 0xFF00) / 0x100);
                data[offset + 0] = (byte)((value & 0xFF) / 0x1);
            }
        }

        /// <summary>
        /// Sets a value into memory at the specified address.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="value"></param>
        /// <param name="endian"></param>
        public static void SetWordUnsafe(VoidPtr address, int value, Endianness endian)
        {
            if (address % 4 != 0)
                return;

            if (endian == Endianness.Big)
                *(bint*)address = (bint)value;
            else
                *(int*)address = value;
        }

        /// <summary>
        /// Sets a floating point value into an array of bytes, resizing if necessary.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="value"></param>
        /// <param name="offset"></param>
        /// <param name="endian"></param>
        public static void SetFloat(ref byte[] data, float value, long offset, Endianness endian)
        {
            SetWord(ref data, FloatToHex(value), offset, endian);
        }

        /// <summary>
        /// Sets a floating point value into memory at the specified address.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="value"></param>
        /// <param name="endian"></param>
        public static void SetFloatUnsafe(VoidPtr address, float value, Endianness endian)
        {
            if (address % 4 != 0)
                return;

            if (endian == Endianness.Big)
                *(bfloat*)address = value;
            else
                *(float*)address = value;
        }

        /// <summary>
        /// Gets a floating point value from a specified adress.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="endian"></param>
        /// <returns></returns>
        public static float GetFloatUnsafe(VoidPtr address, Endianness endian)
        {
            if (address % 4 != 0)
                return 0;

            if (endian == Endianness.Big)
                return *(bfloat*)address;
            else
                return *(float*)address;
        }

        /// <summary>
        /// Returns the hexadecimal representation of the passed in float value.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static long FloatToHex(float val)
        {
            if (val == 0) return 0;
            long sign = (val >= 0 ? 0 : 8);
            long exponent = 0x7F;
            float mantissa = Math.Abs(val);

            if (mantissa > 1)
            {
                while (mantissa > 2)
                { mantissa /= 2; exponent++; }
            }
            else
            {
                while (mantissa < 1)
                { mantissa *= 2; exponent--; }
            }

            mantissa -= 1;
            mantissa *= (float)Math.Pow(2, 23);

            return (
                  sign * 0x10000000
                + exponent * 0x800000
                + (long)mantissa);
        }

        /// <summary>
        /// Returns the floating point value of the passed in hexadecimal value.
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        public static float HexToFloat(long val)
        {
            if (val == 0) return 0;
            float sign = ((val & 0x80000000) == 0 ? 1 : -1);
            int exponent = ((int)(val & 0x7F800000) / 0x800000) - 0x7F;
            float mantissa = (val & 0x7FFFFF);
            long mantissaBits = 23;

            if (mantissa != 0)
            {
                while (((long)mantissa & 0x1) != 1)
                { mantissa /= 2; mantissaBits--; }
            }

            mantissa /= (float)Math.Pow(2, mantissaBits);
            mantissa += 1;

            mantissa *= (float)Math.Pow(2, exponent);
            return mantissa *= sign;
        }

        /// <summary>
        /// Copies data from memory at a specific address into an array
        /// </summary>
        /// <param name="source"></param>
        /// <param name="sourceOffset"></param>
        /// <param name="target"></param>
        /// <param name="targetOffset"></param>
        /// <param name="Length"></param>
        public static byte[] GetArrayFromAddress(VoidPtr address, int length)
        {
            byte[] arr = new byte[length];
            for (int i = 0; i < length; i++)
                arr[i] = *(byte*)(address + i);
            return arr;
        }

        /// <summary>
        /// Returns a string from an array of bytes at the specified offset
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="endian"></param>
        /// <returns></returns>
        public static string GetString(byte[] data, long offset)
        {
            if (offset >= data.Length) throw new Exception("Offset outside of expected value range.");
            string s = string.Empty;

            while (data[offset] != 0)
                s += (char)data[offset++];

            return s;
        }

        public static string StartProcess(string application, string args)
        {
            var sb = new StringBuilder();
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = application,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();


            //while (!proc.StandardError.EndOfStream)
            //sb.Append(proc.StandardError.ReadLine() + "\n");
            //while (!proc.StandardOutput.EndOfStream)
            //sb.Append(proc.StandardOutput.ReadLine() + "\n");
            sb.Append(proc.StandardError.ReadToEnd());
            sb.Append(proc.StandardOutput.ReadToEnd());

            proc.WaitForExit();
            proc.Close();

            return sb.ToString();
        }
        public static string GetHex(string filepath, out int lineCount)
        {
            string text = GetHex(filepath);
            lineCount = text.Trim().Split('\n').Length;
            return text;
        }
        public static string GetHex(string filepath)
        {
            StringBuilder b = new StringBuilder();
            using (var stream = File.Open(filepath, FileMode.Open, FileAccess.Read))
            {
                using (var reader = new BinaryReader(stream))
                {
                    while (!(stream.Position == stream.Length))
                    {
                        if (stream.Length - stream.Position >= 8)
                        {
                            b.Append(reader.ReadUInt32().Reverse().ToString("X8"));
                            b.Append(' ');
                            b.Append(reader.ReadUInt32().Reverse().ToString("X8"));
                            b.Append('\n');
                        }
                        else
                        {
                            b.Append(reader.ReadUInt32().Reverse().ToString("X8"));
                            b.Append(' ');
                            b.Append("00000000");
                            b.Append('\n');
                        }
                    }
                }
            }
            return b.ToString();
        }
    }

    public enum Endianness
    {
        Big = 0,
        Little = 1
    }
}
