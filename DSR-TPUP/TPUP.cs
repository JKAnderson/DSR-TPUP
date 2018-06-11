using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

        private readonly bool repack;
        private readonly string gameDir, looseDir;
        private readonly object countLock, progressLock, writeLock;
        private int progress, progressMax;
        private bool stop;
        private Thread[] threads;
        private List<string> log;
        private List<(bool, string)> errors;
        private int fileCount, textureCount;
        private Dictionary<string, (DXGIFormat, int, int)> reports;

        public TPUP(string setGameDir, string setLooseDir, bool setRepack, int threadCount)
        {
            stop = false;
            gameDir = Path.GetFullPath(setGameDir);
            looseDir = Path.GetFullPath(setLooseDir);
            repack = setRepack;
            log = new List<string>();
            errors = new List<(bool, string)>();
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

        public int GetLogLength()
        {
            lock (log)
                return log.Count;
        }

        public string GetLogLine(int i)
        {
            lock (log)
                return log[i];
        }

        public int GetErrorLength()
        {
            lock (errors)
                return errors.Count;
        }

        public (bool, string) GetErrorLine(int i)
        {
            lock (errors)
                return errors[i];
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
            List<string> filepaths = new List<string>();
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
                    filepaths.Add(filepath);
            }
            filepaths.Reverse();
            progressMax = filepaths.Count;

            if (repack)
                appendLog("Checking {0} files for repacking...", filepaths.Count);
            else
            {
                appendLog("Unpacking {0} files...", filepaths.Count);
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
                                        Path.GetFileName(filepath), printDXGIFormat(dds.Item1), dds.Item2, dds.Item3);
                                }
                                else
                                    sb.AppendFormat("File:   {0}\r\nFormat: Unknown\r\nSize:   Unknown\r\n\r\n");
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
                    appendLog("Repacked {0} textures in {1} files!", textureCount, fileCount);
                else
                    appendLog("Unpacked {0} textures from {1} files!", textureCount, fileCount);
            }
        }

        private void iterateFiles(List<string> filepaths)
        {
            bool empty = false;
            while (!empty && !stop)
            {
                string filepath = null;
                lock (filepaths)
                {
                    if (filepaths.Count == 0)
                        empty = true;
                    else
                    {
                        filepath = filepaths.Last();
                        filepaths.RemoveAt(filepaths.Count - 1);
                    }
                }

                if (filepath != null)
                {
                    string absolute = Path.GetFullPath(filepath);
                    string relative = absolute.Substring(gameDir.Length + 1);
                    string extension = Path.GetExtension(absolute);
                    string decompressedExtension = extension;
                    if (extension == ".dcx")
                        decompressedExtension = Path.GetExtension(absolute.Substring(0, absolute.Length - 4));

                    DCX dcx = null;
                    if (repack)
                        appendLog("Checking: " + relative);
                    else
                        appendLog("Unpacking: " + relative);

                    byte[] bytes = File.ReadAllBytes(absolute);
                    if (extension == ".dcx")
                    {
                        dcx = new DCX(bytes);
                        bytes = dcx.Decompressed;
                    }

                    string subpath = relative.Substring(0, relative.Length - extension.Length);
                    if (dcx != null)
                        subpath = subpath.Substring(0, subpath.Length - decompressedExtension.Length);

                    bool edited = false;
                    switch (decompressedExtension)
                    {
                        case ".tpf":
                            TPF tpf = new TPF(bytes);
                            if (processTPF(tpf, looseDir, subpath, repack))
                            {
                                edited = true;
                                byte[] tpfBytes = tpf.Repack();
                                if (dcx != null)
                                {
                                    dcx.Decompressed = tpfBytes;
                                    tpfBytes = dcx.Compress();
                                }
                                writeRepack(absolute, tpfBytes);
                                lock (countLock)
                                    fileCount++;
                            }
                            break;

                        case ".tpfbhd":
                            BHD bhd = new BHD(bytes);
                            string dir = Path.GetDirectoryName(absolute);
                            string name = Path.GetFileNameWithoutExtension(absolute);
                            string bdtPath = dir + "\\" + name + ".tpfbdt";
                            if (File.Exists(bdtPath))
                            {
                                byte[] bdtBytes = File.ReadAllBytes(bdtPath);
                                BDT bdt = new BDT(bdtBytes, bhd);
                                if (processBHD(bhd, bdt, looseDir, subpath, repack))
                                {
                                    edited = true;
                                    (byte[], byte[]) repacked = bhd.Repack(bdt);
                                    if (dcx != null)
                                    {
                                        dcx.Decompressed = repacked.Item1;
                                        repacked.Item1 = dcx.Compress();
                                    }
                                    writeRepack(absolute, repacked.Item1);
                                    writeRepack(bdtPath, repacked.Item2);
                                    lock (countLock)
                                        fileCount++;
                                }
                            }
                            else
                                throw new FileNotFoundException("Data file not found for header: " + relative);
                            break;

                        case ".chrbnd":
                        case ".ffxbnd":
                        case ".fgbnd":
                        case ".objbnd":
                        case ".partsbnd":
                            BND bnd = new BND(bytes);
                            foreach (BNDEntry entry in bnd.Files)
                            {
                                if (stop)
                                    break;

                                string entryExtension = Path.GetExtension(entry.Filename);
                                if (entryExtension == ".tpf")
                                {
                                    TPF bndTPF = new TPF(entry.Bytes);
                                    if (processTPF(bndTPF, looseDir, subpath, repack))
                                    {
                                        entry.Bytes = bndTPF.Repack();
                                        edited = true;
                                    }
                                }
                                else if (entryExtension == ".chrtpfbhd")
                                {
                                    BHD bndBHD = new BHD(entry.Bytes);
                                    string bndDir = Path.GetDirectoryName(absolute);
                                    string bndName = Path.GetFileNameWithoutExtension(absolute);
                                    if (dcx != null)
                                        bndName = Path.GetFileNameWithoutExtension(bndName);
                                    string bndBDTPath = bndDir + "\\" + bndName + ".chrtpfbdt";
                                    if (File.Exists(bndBDTPath))
                                    {
                                        byte[] bdtBytes = File.ReadAllBytes(bndBDTPath);
                                        BDT bndBDT = new BDT(bdtBytes, bndBHD);
                                        if (processBHD(bndBHD, bndBDT, looseDir, subpath, repack))
                                        {
                                            (byte[], byte[]) repacked = bndBHD.Repack(bndBDT);
                                            entry.Bytes = repacked.Item1;
                                            writeRepack(bndBDTPath, repacked.Item2);
                                            edited = true;
                                        }
                                    }
                                    else
                                        throw new FileNotFoundException("Data file not found for header: " + relative);
                                }
                            }

                            if (edited && !stop)
                            {
                                byte[] bndBytes = bnd.Repack();
                                if (dcx != null)
                                {
                                    dcx.Decompressed = bndBytes;
                                    bndBytes = dcx.Compress();
                                }
                                writeRepack(absolute, bndBytes);
                                lock (countLock)
                                    fileCount++;
                            }
                            break;
                    }

                    if (repack && !edited)
                        appendError(false, "Warning: {0}\r\n\u2514\u2500 No overrides found.", relative);

                    lock (progressLock)
                        progress++;
                }
            }
        }

        private bool processBHD(BHD bhd, BDT bdt, string baseDir, string subpath, bool repack)
        {
            bool edited = false;
            foreach (BDTEntry bdtEntry in bdt.Files)
            {
                if (stop)
                    return false;

                DCX bdtDCX = null;
                byte[] bdtEntryBytes = bdtEntry.Bytes;
                string bdtEntryExtension = Path.GetExtension(bdtEntry.Filename);
                if (bdtEntryExtension == ".dcx")
                {
                    bdtDCX = new DCX(bdtEntryBytes);
                    bdtEntryBytes = bdtDCX.Decompressed;
                    bdtEntryExtension = Path.GetExtension(bdtEntry.Filename.Substring(0, bdtEntry.Filename.Length - 4));
                }

                if (bdtEntryExtension == ".tpf")
                {
                    TPF tpf = new TPF(bdtEntryBytes);
                    if (processTPF(tpf, baseDir, subpath, repack))
                    {
                        bdtEntry.Bytes = tpf.Repack();
                        if (bdtDCX != null)
                        {
                            bdtDCX.Decompressed = bdtEntry.Bytes;
                            bdtEntry.Bytes = bdtDCX.Compress();
                        }
                        edited = true;
                    }
                }
            }
            return edited;
        }

        private bool processTPF(TPF tpf, string baseDir, string subDir, bool repack)
        {
            if (!repack && tpf.Files.Count > 0)
                Directory.CreateDirectory(baseDir + "\\" + subDir);

            List<string> names = new List<string>();
            List<string> dupes = new List<string>();
            foreach (TPFEntry tpfEntry in tpf.Files)
            {
                if (names.Contains(tpfEntry.Name))
                    dupes.Add(tpfEntry.Name);
                else
                    names.Add(tpfEntry.Name);
            }

            bool edited = false;
            for (int i = 0; i < tpf.Files.Count; i++)
            {
                if (stop)
                    return false;

                TPFEntry tpfEntry = tpf.Files[i];
                string name = tpfEntry.Name;
                if (dupes.Contains(name))
                    name += "_" + i;
                string subPath = subDir + "\\" + name + ".dds";
                string ddsPath = baseDir + "\\" + subPath;

                if (repack)
                {
                    if (!File.Exists(ddsPath) && File.Exists(ddsPath + "2"))
                        ddsPath += "2";

                    if (File.Exists(ddsPath))
                    {
                        byte[] ddsBytes = File.ReadAllBytes(ddsPath);
                        DXGIFormat originalFormat = DDSFile.Read(new MemoryStream(tpfEntry.Bytes)).Format;
                        DXGIFormat newFormat = DDSFile.Read(new MemoryStream(ddsBytes)).Format;

                        if (originalFormat == DXGIFormat.Unknown)
                            appendError(true, "Error: {0}\r\n\u2514\u2500 Could not determine format of game file.", subPath);

                        if (newFormat == DXGIFormat.Unknown)
                            appendError(true, "Error: {0}\r\n\u2514\u2500 Could not determine format of override file.", subPath);

                        if (originalFormat != DXGIFormat.Unknown && newFormat != DXGIFormat.Unknown && originalFormat != newFormat)
                        {
                            appendError(false, "Warning: {0}\r\n\u2514\u2500 Expected format {1}, got format {2}. Converting...",
                                    subPath, printDXGIFormat(originalFormat), printDXGIFormat(newFormat));

                            byte[] newBytes = convertFile(ddsPath, originalFormat);
                            if (newBytes != null)
                                ddsBytes = newBytes;
                        }

                        tpfEntry.Bytes = ddsBytes;
                        edited = true;
                        lock (countLock)
                            textureCount++;
                    }
                }
                else
                {
                    MemoryStream stream = new MemoryStream(tpfEntry.Bytes);
                    DDSContainer dds = DDSFile.Read(stream);
                    if (dds.Format == DXGIFormat.Unknown || dds.MipChains.Count < 1 || dds.MipChains[0].Count < 1)
                        appendError(true, "Error: {0}\r\n\u2514\u2500 Could not determine format of game file.", subPath);
                    else
                        reports[ddsPath] = (dds.Format, dds.MipChains[0][0].Width, dds.MipChains[0][0].Height);

                    lock (writeLock)
                    {
                        if (!File.Exists(ddsPath))
                        {
                            File.WriteAllBytes(ddsPath, tpfEntry.Bytes);
                            lock (countLock)
                                textureCount++;
                        }
                        else
                            appendError(true, "Error: {0}\r\n\u2514\u2500 Duplicate file found.", subPath);
                    }
                }
            }

            return edited;
        }

        private byte[] convertFile(string filepath, DXGIFormat format)
        {
            if (!File.Exists("bin\\texconv.exe"))
            {
                appendError(true, "Error: texconv.exe not found in bin folder");
                return null;
            }

            filepath = Path.GetFullPath(filepath);
            string directory = Path.GetDirectoryName(filepath);
            string filename = Path.GetFileName(filepath);
            string noExtension = Path.GetFileNameWithoutExtension(filename);
            string outPath = directory + "\\texconv_" + noExtension + ".dds";
            if (File.Exists(outPath))
                File.Delete(outPath);

            string args = string.Format("-px texconv_ -f {0} -o \"{1}\" \"{1}\\{2}\"",
                printDXGIFormat(format), directory, filename);
            ProcessStartInfo startInfo = new ProcessStartInfo("bin\\texconv.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            Process texconv = Process.Start(startInfo);
            texconv.WaitForExit();

            byte[] result = null;
            if (!File.Exists(outPath))
            {
                appendError(true, "Error: {0}\\{1}\r\n\u2514\u2500 Conversion failed.", directory, filename);
            }
            else
            {
                result = File.ReadAllBytes(outPath);
                File.Delete(outPath);
            }
            return result;
        }

        private void appendLog(string format, params object[] args)
        {
            string line = string.Format(format, args);
            lock (log)
                log.Add(line);
        }

        private void appendError(bool error, string format, params object[] args)
        {
            lock (errors)
                errors.Add((error, string.Format(format, args)));
        }

        private static void writeRepack(string path, byte[] bytes)
        {
            if (!File.Exists(path + ".tpupbak"))
                File.Copy(path, path + ".tpupbak");
            File.WriteAllBytes(path, bytes);
        }

        private static Dictionary<DXGIFormat, string> dxgiFormatOverride = new Dictionary<DXGIFormat, string>()
        {
            [DXGIFormat.BC1_UNorm] = "DXT1",
            [DXGIFormat.BC2_UNorm] = "DXT3",
            [DXGIFormat.BC3_UNorm] = "DXT5",
            [DXGIFormat.Opaque_420] = "420_OPAQUE",
        };

        private static string printDXGIFormat(DXGIFormat format)
        {
            if (dxgiFormatOverride.ContainsKey(format))
                return dxgiFormatOverride[format];
            else
                return format.ToString().ToUpper();
        }
    }
}
