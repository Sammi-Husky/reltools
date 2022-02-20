using BrawlLib.Internal;
using System.Diagnostics;
using System.Text;

namespace reltools
{
    public struct ProcResult
    {
        public ProcResult(string output, string error, int errStatus)
        {
            StandardOutput = output;
            StandardError = error;
            ExitCode = errStatus;
        }
        public readonly string StandardOutput;
        public readonly string StandardError;
        public readonly int ExitCode;
    }
    public static class Util
    {
        public static bool IsAscii(byte data)
        {
            return data >= 0x20 && data <= 0x7e;
        }
        public static bool IsString(byte[] data)
        {
            for(int i=0; i<data.Length; i++)
            {
                if(!IsAscii(data[i]))
                    return false;
            }
            return true;
        }
        public static ProcResult StartProcess(string application, params string[] args)
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = application,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();

            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            int exitCode = proc.ExitCode;
            proc.Close();
            return new ProcResult(output, error, exitCode);
        }
    }
}
