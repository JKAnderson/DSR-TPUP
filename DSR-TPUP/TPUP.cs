using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DSR_TPUP
{
    class TPUP
    {
        // BNDs without texture files:
        // anibnd, luabnd, menuesdbnd, msgbnd, mtdbnd, parambnd, paramdefbnd, remobnd, rumblebnd

        private static string[] validExtensions =
        {
            ".chrbnd",
            ".ffxbnd",
            ".fgbnd",
            ".objbnd",
            ".partsbnd",
            ".tpf",
            ".tpfbhd",
        };

        private const string INTERROOT = "N:\\FRPG\\data\\INTERROOT_x64\\";

        private List<string> conflicts = new List<string>();

        private List<string> log;
        private Thread[] threads;
        private object writeLock = new object();

        public bool Stop = false;

        public TPUP(int threadCount)
        {
            log = new List<string>();
            threads = new Thread[threadCount];
        }

        public void ProcessFile(string gameDir, string looseDir, bool repack)
        {
            gameDir = Path.GetFullPath(gameDir);
            looseDir = Path.GetFullPath(looseDir);
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

            for (int i = 0; i < threads.Length; i++)
            {
                Thread thread = new Thread(() => iterateFiles(gameDir, looseDir, filepaths, repack));
                threads[i] = thread;
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();
        }

        private void iterateFiles(string gameDir, string targetDir, List<string> filepaths, bool repack)
        {
            bool empty = false;
            while (!empty && !Stop)
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
                        AppendLog("Checking: " + relative);
                    else
                        AppendLog("Unpacking: " + relative);

                    byte[] bytes = File.ReadAllBytes(absolute);
                    if (extension == ".dcx")
                    {
                        dcx = new DCX(bytes);
                        bytes = dcx.Decompressed;
                    }

                    string subpath = relative.Substring(0, relative.Length - extension.Length);
                    if (dcx != null)
                        subpath = subpath.Substring(0, subpath.Length - decompressedExtension.Length);

                    switch (decompressedExtension)
                    {
                        case ".tpf":
                            TPF tpf = new TPF(bytes);
                            if (processTPF(tpf, targetDir + "\\" + subpath, repack))
                            {
                                byte[] tpfBytes = tpf.Repack();
                                if (dcx != null)
                                {
                                    dcx.Decompressed = tpfBytes;
                                    tpfBytes = dcx.Compress();
                                }
                                writeRepack(absolute, tpfBytes);
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
                                if (processBHD(bhd, bdt, targetDir + "\\" + subpath, repack))
                                {
                                    (byte[], byte[]) repacked = bhd.Repack(bdt);
                                    if (dcx != null)
                                    {
                                        dcx.Decompressed = repacked.Item1;
                                        repacked.Item1 = dcx.Compress();
                                    }
                                    writeRepack(absolute, repacked.Item1);
                                    writeRepack(bdtPath, repacked.Item2);
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
                            bool edited = false;
                            foreach (BNDEntry entry in bnd.Files)
                            {
                                string entryExtension = Path.GetExtension(entry.Filename);
                                if (entryExtension == ".tpf")
                                {
                                    string subsubpath = subpath + "\\" + Path.GetFileNameWithoutExtension(entry.Filename);
                                    TPF bndTPF = new TPF(entry.Bytes);
                                    if (processTPF(bndTPF, targetDir + "\\" + subpath, repack))
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
                                        if (processBHD(bndBHD, bndBDT, targetDir + "\\" + subpath, repack))
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

                            if (edited)
                            {
                                byte[] bndBytes = bnd.Repack();
                                if (dcx != null)
                                {
                                    dcx.Decompressed = bndBytes;
                                    bndBytes = dcx.Compress();
                                }
                                writeRepack(absolute, bndBytes);
                            }
                            break;
                    }
                }
            }
        }

        private void writeRepack(string path, byte[] bytes)
        {
            if (!File.Exists(path + ".tpupbak"))
                File.Copy(path, path + ".tpupbak");
            File.WriteAllBytes(path, bytes);
        }

        private bool processBHD(BHD bhd, BDT bdt, string directory, bool repack)
        {
            bool edited = false;
            foreach (BDTEntry bdtEntry in bdt.Files)
            {
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
                    if (processTPF(tpf, directory, repack))
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

        private bool processTPF(TPF tpf, string directory, bool repack)
        {
            if (!repack && tpf.Files.Count > 0)
                Directory.CreateDirectory(directory);

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
                TPFEntry tpfEntry = tpf.Files[i];
                string name = tpfEntry.Name;
                if (dupes.Contains(name))
                    name += "_" + i;
                string ddsPath = directory + "\\" + name + ".dds";
                string pngPath = directory + "\\" + name + ".png";

                lock (writeLock)
                {
                    if (repack)
                    {
                        byte[] ddsBytes = null;
                        DDS.DXGI_FORMAT originalFormat = DDS.GetFormat(tpfEntry.Bytes);
                        if (File.Exists(ddsPath) || File.Exists(ddsPath + "2"))
                        {
                            ddsBytes = File.Exists(ddsPath) ? File.ReadAllBytes(ddsPath) : File.ReadAllBytes(ddsPath + "2");
                            DDS.DXGI_FORMAT newFormat = DDS.GetFormat(ddsBytes);
                            if (originalFormat != newFormat)
                            {
                                AppendLog("Warning: expected format {0}, got format {1}. Converting {2}...", originalFormat, newFormat, ddsPath);
                                ddsBytes = convertFile(directory, name + ".dds", originalFormat, false);
                            }
                        }
                        else if (File.Exists(pngPath))
                        {
                            AppendLog("Warning: .png is supported, but not recommended. Converting {0}...", pngPath);
                            ddsBytes = convertFile(directory, name + ".png", originalFormat, true);
                        }

                        if (ddsBytes != null)
                        {
                            tpfEntry.Bytes = ddsBytes;
                            edited = true;
                        }
                    }
                    else
                    {
                        if (!File.Exists(ddsPath))
                            File.WriteAllBytes(ddsPath, tpfEntry.Bytes);
                        else
                            throw null;
                    }
                }
            }

            return edited;
        }

        private byte[] convertFile(string directory, string filename, DDS.DXGI_FORMAT format, bool srgb)
        {
            if (!File.Exists("bin\\texconv.exe"))
                throw new FileNotFoundException("texconv.exe not found in bin folder");

            string noExtension = Path.GetFileNameWithoutExtension(filename);
            string outPath = directory + "\\texconv_" + noExtension + ".dds";
            if (File.Exists(outPath))
                File.Delete(outPath);

            string args;
            if (srgb)
                args = "-px texconv_ -srgbo -f {0} -o \"{1}\" \"{1}\\{2}\"";
            else
                args = "-px texconv_ -f {0} -o \"{1}\" \"{1}\\{2}\"";
            args = string.Format(args, format, directory, filename);
            ProcessStartInfo startInfo = new ProcessStartInfo("bin\\texconv.exe", args);
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            Process texconv = Process.Start(startInfo);
            texconv.WaitForExit();

            byte[] result = null;
            if (!File.Exists(outPath))
            {
                AppendLog("Conversion failed: {0}\\{1}", directory, filename);
            }
            else
            {
                result = File.ReadAllBytes(outPath);
                File.Delete(outPath);
            }
            return result;
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

        private void AppendLog(string format, params object[] args)
        {
            string line = string.Format(format, args);
            lock (log)
                log.Add(line);
        }
    }
}
