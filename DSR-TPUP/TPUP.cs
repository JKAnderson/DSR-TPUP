using System.Collections.Generic;
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

        public void Process(string gameDir, string looseDir, bool repack)
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
                    AppendLog("Processing: " + relative);

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
                                if (!File.Exists(absolute + ".tpupbak"))
                                    File.Copy(absolute, absolute + ".tpupbak");
                                File.WriteAllBytes(absolute, tpfBytes);
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
                                    if (!File.Exists(absolute + ".tpupbak"))
                                        File.Copy(absolute, absolute + ".tpupbak");
                                    if (!File.Exists(bdtPath + ".tpupbak"))
                                        File.Copy(bdtPath, bdtPath + ".tpupbak");
                                    File.WriteAllBytes(absolute, repacked.Item1);
                                    File.WriteAllBytes(bdtPath, repacked.Item2);
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
                                            if (!File.Exists(bndBDTPath + ".tpupbak"))
                                                File.Copy(bndBDTPath, bndBDTPath + ".tpupbak");
                                            File.WriteAllBytes(bndBDTPath, repacked.Item2);
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
                                if (!File.Exists(absolute + ".tpupbak"))
                                    File.Copy(absolute, absolute + ".tpupbak");
                                File.WriteAllBytes(absolute, bndBytes);
                            }
                            break;
                    }
                }
            }
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
                lock (writeLock)
                {
                    string name = tpfEntry.Name;
                    if (dupes.Contains(name))
                        name += "_" + i;
                    string path = directory + "\\" + name + ".dds";

                    if (repack)
                    {
                        if (File.Exists(path))
                        {
                            tpfEntry.Bytes = File.ReadAllBytes(path);
                            edited = true;
                        }
                    }
                    else
                    {
                        if (!File.Exists(path))
                            File.WriteAllBytes(path, tpfEntry.Bytes);
                        else
                            throw null;
                    }
                }
            }

            return edited;
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

        private void AppendLog(string line)
        {
            lock (log)
                log.Add(line);
        }
    }
}
