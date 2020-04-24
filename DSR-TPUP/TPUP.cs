﻿using DSFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using TeximpNet.DDS;

namespace DSR_TPUP
{
    class TPUP
    {
        // BNDs without texture files:
        // anibnd, luabnd, menuesdbnd, msgbnd, mtdbnd, parambnd, paramdefbnd, remobnd, rumblebnd

        private static readonly string[] validExtensions =
        {
            ".chrbnd",
            ".ffxbnd",
            ".fgbnd",
            ".objbnd",
            ".partsbnd",
            ".tpf",
            ".tpfbhd",
        };

        private const string TEXCONV_PATH = @"bin\texconv.exe";
        private const string TEXCONV_SUFFIX = "_autoconvert";

        private readonly bool repack;
        private readonly string gameDir, looseDir;
        private readonly object countLock, progressLock, writeLock;
        private int progress, progressMax;
        private bool stop, conversionWarning, preserveConverted;
        private Thread[] threads;
        private int fileCount, textureCount;
        private Dictionary<string, (DXGIFormat, int, int)> reports;

        public ConcurrentQueue<string> Log, Error;

        public TPUP(string setGameDir, string setLooseDir, bool setRepack, bool setPreserve, int threadCount)
        {
            stop = false;
            conversionWarning = false;
            preserveConverted = setPreserve;

            if (setGameDir.EndsWith("\\"))
                gameDir = setGameDir.Substring(0, setGameDir.Length-1);
            else
                gameDir = Path.GetFullPath(setGameDir);

            if (setLooseDir.EndsWith("\\"))
                looseDir = setLooseDir.Substring(0, setLooseDir.Length - 1);
            else
                looseDir = Path.GetFullPath(setLooseDir);

            repack = setRepack;
            Log = new ConcurrentQueue<string>();
            Error = new ConcurrentQueue<string>();
            threads = new Thread[threadCount];
            writeLock = new object();
            fileCount = 0;
            textureCount = 0;
            countLock = new object();
            reports = new Dictionary<string, (DXGIFormat, int, int)>();
            progress = 0;
            progressMax = 0;
            progressLock = new object();
        }

        public void Stop()
        {
            appendLog("Stopping...");
            stop = true;
        }

        public int GetProgressMax()
        {
            return progressMax;
        }

        public int GetProgress()
        {
            lock (progressLock)
                return progress;
        }

        public void Start()
        {
            ConcurrentQueue<string> filepaths = new ConcurrentQueue<string>();
            foreach (string filepath in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
            {
                string decompressedExtension = Path.GetExtension(filepath);
                if (decompressedExtension == ".dcx")
                    decompressedExtension = Path.GetExtension(Path.GetFileNameWithoutExtension(filepath));

                bool valid = false;
                if (validExtensions.Contains(decompressedExtension))
                {
                    valid = true;
                    if (repack)
                    {
                        string relative = Path.GetDirectoryName(filepath.Substring(gameDir.Length + 1));
                        string filename = Path.GetFileNameWithoutExtension(filepath);
                        if (Path.GetExtension(filepath) == ".dcx")
                            filename = Path.GetFileNameWithoutExtension(filename);
                        string outputPath = looseDir + "\\" + relative + "\\" + filename;
                        if (!Directory.Exists(outputPath) || Directory.GetFiles(outputPath).Length == 0)
                            valid = false;
                    }
                }

                if (valid)
                    filepaths.Enqueue(filepath);
            }
            progressMax = filepaths.Count;

            if (repack)
                appendLog("Checking {0:n0} files for repacking...", filepaths.Count);
            else
            {
                appendLog("Unpacking {0:n0} files...", filepaths.Count);
                fileCount = filepaths.Count;
            }

            for (int i = 0; i < threads.Length; i++)
            {
                Thread thread = new Thread(() => iterateFiles(filepaths));
                threads[i] = thread;
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();

            if (!stop)
            {
                if (!repack)
                {
                    appendLog("Generating reports...");
                    foreach (string dirPath in Directory.EnumerateDirectories(looseDir, "*", SearchOption.AllDirectories))
                    {
                        if (Directory.GetDirectories(dirPath).Length == 0 && Directory.GetFiles(dirPath).Length > 0)
                        {
                            StringBuilder sb = new StringBuilder();
                            foreach (string filepath in Directory.GetFiles(dirPath))
                            {
                                if (reports.ContainsKey(filepath))
                                {
                                    (DXGIFormat, int, int) dds = reports[filepath];
                                    sb.AppendFormat("File:   {0}\r\nFormat: {1}\r\nSize:   {2}x{3}\r\n\r\n",
                                        Path.GetFileName(filepath), PrintDXGIFormat(dds.Item1), dds.Item2, dds.Item3);
                                }
                                else
                                    sb.AppendFormat("File:   {0}\r\nFormat: Unknown\r\nSize:   Unknown\r\n\r\n",
                                        Path.GetFileName(filepath));
                            }
                            File.WriteAllText(dirPath + "\\_report.txt", sb.ToString().TrimEnd());
                        }
                    }
                }
            }

            if (stop)
            {
                if (repack)
                    appendLog("Repacking stopped.");
                else
                    appendLog("Unpacking stopped.");
            }
            else
            {
                if (repack)
                    appendLog("Repacked {0:n0} textures in {1:n0} files!", textureCount, fileCount);
                else
                    appendLog("Unpacked {0:n0} textures from {1:n0} files!", textureCount, fileCount);

                if (conversionWarning)
                    appendError("Some incorrectly formatted files were found while repacking; they have been automatically converted to the correct formats.\r\n"
                        + "If you are a mod user, you don't need to do anything; in most cases they will work fine in-game.\r\n"
                        + "If you are a mod author, please convert your source textures beforehand, as the automatic conversion is slow and not completely reliable.");
            }
        }

        private void iterateFiles(ConcurrentQueue<string> filepaths)
        {
            while (!stop && filepaths.TryDequeue(out string filepath))
            {
                // These are already full paths, but trust no one, not even yourself
                string absolute = Path.GetFullPath(filepath);
                string relative = absolute.Substring(gameDir.Length + 1);

                if (repack)
                    appendLog("Checking: " + relative);
                else
                    appendLog("Unpacking: " + relative);

                bool dcx = false;
                string extension = Path.GetExtension(absolute);
                string subpath = Path.GetDirectoryName(relative) + "\\" + Path.GetFileNameWithoutExtension(absolute);
                if (extension == ".dcx")
                {
                    dcx = true;
                    extension = Path.GetExtension(Path.GetFileNameWithoutExtension(absolute));
                    subpath = subpath.Substring(0, subpath.Length - extension.Length);
                }

                try
                {
                    bool edited = false;
                    switch (extension)
                    {
                        case ".tpf":
                            edited = prepareTPF(absolute, relative, subpath, dcx);
                            break;

                        case ".tpfbhd":
                            edited = prepareBHD(absolute, relative, subpath, dcx);
                            break;

                        case ".chrbnd":
                        case ".ffxbnd":
                        case ".fgbnd":
                        case ".objbnd":
                        case ".partsbnd":
                            edited = prepareBND(absolute, relative, subpath, dcx);
                            break;
                    }

                    if (edited)
                    {
                        lock (countLock)
                            fileCount++;
                    }
                    else if (repack && !stop)
                    {
                        appendError("Notice: {0}\r\n\u2514\u2500 No overrides found.", relative);
                    }
                }
                catch (Exception ex)
                {
                    Stop();
                    MessageBox.Show("Fatal error encountered. The operation will now terminate.\n\n" + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                lock (progressLock)
                    progress++;
            }
        }

        private bool prepareTPF(string absolute, string relative, string subpath, bool dcx)
        {
            bool edited = false;
            TPF tpf;
            if (dcx)
            {
                byte[] decompressed = DCX.Decompress(absolute);
                tpf = TPF.Read(decompressed);
            }
            else
            {
                tpf = TPF.Read(absolute);
            }

            if (processTPF(tpf, looseDir, subpath, repack))
            {
                edited = true;
                backup(absolute);

                if (dcx)
                {
                    byte[] tpfBytes = tpf.Write();
                    DCX.Compress(tpfBytes, absolute);
                }
                else
                {
                    tpf.Write(absolute);
                }
            }
            return edited;
        }

        private bool prepareBHD(string absolute, string relative, string subpath, bool dcx)
        {
            bool edited = false;
            string dir = Path.GetDirectoryName(absolute);
            string name = Path.GetFileNameWithoutExtension(absolute);
            string bdtPath = dir + "\\" + name + ".tpfbdt";
            if (File.Exists(bdtPath))
            {
                BDT bdt;
                if (dcx)
                {
                    byte[] decompressed = DCX.Decompress(absolute);
                    bdt = BDT.Read(decompressed, bdtPath);
                }
                else
                {
                    bdt = BDT.Read(absolute, bdtPath);
                }

                if (processBDT(bdt, looseDir, subpath, repack))
                {
                    edited = true;
                    backup(absolute);
                    backup(bdtPath);

                    if (dcx)
                    {
                        byte[] bhdBytes = bdt.Write(bdtPath);
                        DCX.Compress(bhdBytes, absolute);
                    }
                    else
                    {
                        bdt.Write(absolute, bdtPath);
                    }
                }
            }
            else
            {
                appendError("Error: {0}\r\n\u2514\u2500 BDT not found: {1}", relative, bdtPath);
            }
            return edited;
        }

        private bool prepareBND(string absolute, string relative, string subpath, bool dcx)
        {
            bool edited = false;
            BND bnd;
            if (dcx)
            {
                byte[] decompressed = DCX.Decompress(absolute);
                bnd = BND.Read(decompressed);
            }
            else
            {
                bnd = BND.Read(absolute);
            }

            foreach (BND.File bndEntry in bnd.Files)
            {
                if (stop)
                    break;

                string entryExtension = Path.GetExtension(bndEntry.Name);
                if (entryExtension == ".tpf")
                {
                    TPF bndTPF = TPF.Read(bndEntry.Bytes);
                    if (processTPF(bndTPF, looseDir, subpath, repack))
                    {
                        edited = true;
                        bndEntry.Bytes = bndTPF.Write();
                    }
                }
                else if (entryExtension == ".chrtpfbhd")
                {
                    string bndDir = Path.GetDirectoryName(absolute);
                    string bndName = Path.GetFileNameWithoutExtension(absolute);
                    if (dcx)
                        bndName = Path.GetFileNameWithoutExtension(bndName);
                    string bndBDTPath = bndDir + "\\" + bndName + ".chrtpfbdt";
                    if (File.Exists(bndBDTPath))
                    {
                        BDT bndBDT = BDT.Read(bndEntry.Bytes, bndBDTPath);
                        if (processBDT(bndBDT, looseDir, subpath, repack))
                        {
                            edited = true;
                            backup(bndBDTPath);
                            bndEntry.Bytes = bndBDT.Write(bndBDTPath);
                        }
                    }
                    else
                    {
                        appendError("Error: {0}\r\n\u2514\u2500 BDT not found: {1}", relative, bndBDTPath);
                    }
                }
            }

            if (edited && !stop)
            {
                backup(absolute);

                if (dcx)
                {
                    byte[] bndBytes = bnd.Write();
                    DCX.Compress(bndBytes, absolute);
                }
                else
                {
                    bnd.Write(absolute);
                }
            }
            return edited;
        }

        private bool processBDT(BDT bdt, string baseDir, string subPath, bool repack)
        {
            bool edited = false;
            foreach (BDT.File file in bdt.Files)
            {
                if (stop)
                    return false;

                bool dcx = false;
                byte[] bdtEntryBytes = file.Bytes;
                string bdtEntryExtension = Path.GetExtension(file.Name);
                if (bdtEntryExtension == ".dcx")
                {
                    dcx = true;
                    bdtEntryBytes = DCX.Decompress(bdtEntryBytes);
                    bdtEntryExtension = Path.GetExtension(file.Name.Substring(0, file.Name.Length - 4));
                }

                if (bdtEntryExtension == ".tpf")
                {
                    TPF tpf = TPF.Read(bdtEntryBytes);
                    if (processTPF(tpf, baseDir, subPath, repack))
                    {
                        file.Bytes = tpf.Write();
                        if (dcx)
                            file.Bytes = DCX.Compress(file.Bytes);
                        edited = true;
                    }
                }
                // This wouldn't really be a problem, but I would like to know about it
                else
                    appendError("Error: {0}\r\n\u2514\u2500 Non-tpf found in tpfbdt: {1}", subPath, file.Name);
            }
            return edited;
        }

        private bool processTPF(TPF tpf, string baseDir, string subDir, bool repack)
        {
            // parts\HR_F_0010 and parts\HR_F_0010_M have duplicate filenames in the same tpf
            // thx QLOC
            List<string> names = new List<string>();
            List<string> dupes = new List<string>();
            foreach (TPF.Texture texture in tpf.Textures)
            {
                if (names.Contains(texture.Name))
                    dupes.Add(texture.Name);
                else
                    names.Add(texture.Name);
            }

            bool edited = false;
            for (int i = 0; i < tpf.Textures.Count; i++)
            {
                if (stop)
                    return false;

                TPF.Texture texture = tpf.Textures[i];
                string name = texture.Name;
                if (dupes.Contains(name))
                    name += "_" + i;
                string subPath = subDir + "\\" + name + ".dds";
                string ddsPath = baseDir + "\\" + subPath;

                if (repack)
                {
                    // Look for files renamed to .dds2 for Paint.NET plugin support
                    if (!File.Exists(ddsPath) && File.Exists(ddsPath + "2"))
                        ddsPath += "2";

                    if (File.Exists(ddsPath))
                    {
                        byte[] ddsBytes = File.ReadAllBytes(ddsPath);
                        DXGIFormat originalFormat = DDSFile.Read(new MemoryStream(texture.Bytes)).Format;
                        DXGIFormat newFormat = DDSFile.Read(new MemoryStream(ddsBytes)).Format;

                        if (originalFormat == DXGIFormat.Unknown)
                            appendError("Error: {0}\r\n\u2514\u2500 Could not determine format of game file.", subPath);

                        if (newFormat == DXGIFormat.Unknown)
                            appendError("Error: {0}\r\n\u2514\u2500 Could not determine format of override file.", subPath);

                        if (originalFormat != DXGIFormat.Unknown && newFormat != DXGIFormat.Unknown && originalFormat != newFormat)
                        {
                            appendError("Warning: {0}\r\n\u2514\u2500 Expected format {1}, got format {2}.",
                                    subPath, PrintDXGIFormat(originalFormat), PrintDXGIFormat(newFormat));

                            conversionWarning = true;
                            byte[] newBytes = convertFile(ddsPath, originalFormat);
                            if (newBytes != null)
                                ddsBytes = newBytes;
                        }

                        texture.Bytes = ddsBytes;
                        edited = true;
                        lock (countLock)
                            textureCount++;
                    }
                }
                else
                {
                    MemoryStream stream = new MemoryStream(texture.Bytes);
                    DDSContainer dds = DDSFile.Read(stream);

                    lock (writeLock)
                    {
                        // Important to do this in the lock, otherwise some reports are lost
                        if (dds.Format == DXGIFormat.Unknown || dds.MipChains.Count < 1 || dds.MipChains[0].Count < 1)
                            appendError("Error: {0}\r\n\u2514\u2500 Could not determine format of game file.", subPath);
                        else
                            reports[ddsPath] = (dds.Format, dds.MipChains[0][0].Width, dds.MipChains[0][0].Height);

                        if (!File.Exists(ddsPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(ddsPath));
                            File.WriteAllBytes(ddsPath, texture.Bytes);
                            lock (countLock)
                                textureCount++;
                        }
                        else
                            appendError("Error: {0}\r\n\u2514\u2500 Duplicate file found.", subPath);
                    }
                }
            }

            return edited;
        }

        private byte[] convertFile(string filepath, DXGIFormat format)
        {
            if (!File.Exists(TEXCONV_PATH))
            {
                appendError("Error: texconv.exe not found.");
                return null;
            }

            filepath = Path.GetFullPath(filepath);
            string directory = Path.GetDirectoryName(filepath);
            string filename = Path.GetFileName(filepath);
            string outPath = string.Format("{0}\\{1}{2}.dds",
                directory, Path.GetFileNameWithoutExtension(filename), TEXCONV_SUFFIX);

            string args = string.Format("-sx {0} -f {1} -o \"{2}\" \"{2}\\{3}\" -y",
                TEXCONV_SUFFIX, PrintDXGIFormat(format), directory, filename);
            ProcessStartInfo startInfo = new ProcessStartInfo(TEXCONV_PATH, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            Process texconv = Process.Start(startInfo);
            texconv.WaitForExit();

            byte[] result = null;
            if (texconv.ExitCode == 0 && File.Exists(outPath))
            {
                result = File.ReadAllBytes(outPath);
                try
                {
                    if (!preserveConverted)
                        File.Delete(outPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    appendError("Error: {0}\r\n\u2514\u2500 Could not delete converted file.", outPath);
                }
            }
            else
                appendError("Error: {0}\\{1}\r\n\u2514\u2500 Conversion failed.", directory, filename);
            return result;
        }

        private void appendLog(string format, params object[] args)
        {
            string line = string.Format(format, args);
            Log.Enqueue(line);
        }

        private void appendError(string format, params object[] args)
        {
            string line = string.Format(format, args);
            Error.Enqueue(line);
        }

        private static void backup(string path)
        {
            if (!File.Exists(path + ".tpupbak"))
                File.Move(path, path + ".tpupbak");
        }

        private static Dictionary<DXGIFormat, string> dxgiFormatOverride = new Dictionary<DXGIFormat, string>()
        {
            [DXGIFormat.BC1_UNorm] = "DXT1",
            [DXGIFormat.BC2_UNorm] = "DXT3",
            [DXGIFormat.BC3_UNorm] = "DXT5",
            // It's 420_OPAQUE officially, but you can't start an enum member with a number
            [DXGIFormat.Opaque_420] = "420_OPAQUE",
        };

        public static string PrintDXGIFormat(DXGIFormat format)
        {
            if (dxgiFormatOverride.ContainsKey(format))
                return dxgiFormatOverride[format];
            else
                return format.ToString().ToUpper();
        }
    }
}
