using Octokit;
using Semver;
using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;

namespace DSR_TPUP
{
    public partial class FormMain : Form
    {
        private const string UPDATE_LINK = "https://www.nexusmods.com/darksoulsremastered/mods/9?tab=files";
        private static Properties.Settings settings = Properties.Settings.Default;

        private TPUP tpup;
        private Thread tpupThread;
        private int logLength, errorLength;
        private bool abort = false;

        public FormMain()
        {
            InitializeComponent();
        }

        private async void FormMain_Load(object sender, EventArgs e)
        {
            Text = "DSR Texture Packer & Unpacker " + System.Windows.Forms.Application.ProductVersion;
            Location = settings.WindowLocation;
            Size = settings.WindowSize;
            if (settings.WindowMaximized)
                WindowState = FormWindowState.Maximized;

            txtGameDir.Text = settings.GameDir;
            txtUnpackDir.Text = settings.UnpackDir;
            txtRepackDir.Text = settings.RepackDir;
            tclMain.SelectedIndex = settings.TabSelected;
            spcLogs.SplitterDistance = settings.SplitterDistance;
            enableControls(true);

            GitHubClient gitHubClient = new GitHubClient(new ProductHeaderValue("DSR-TPUP"));
            try
            {
                Release release = await gitHubClient.Repository.Release.GetLatest("JKAnderson", "DSR-TPUP");
                if (SemVersion.Parse(release.TagName) > System.Windows.Forms.Application.ProductVersion)
                {
                    lblUpdate.Visible = false;
                    LinkLabel.Link link = new LinkLabel.Link();
                    link.LinkData = UPDATE_LINK;
                    llbUpdate.Links.Add(link);
                    llbUpdate.Visible = true;
                }
                else
                {
                    lblUpdate.Text = "App up to date";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is ApiException || ex is ArgumentException)
            {
                lblUpdate.Text = "Update status unknown";
            }
        }

        private void llbUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(e.Link.LinkData.ToString());
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (tpupThread?.IsAlive ?? false)
            {
                tpup.Stop();
                e.Cancel = true;
                abort = true;
            }
            else
            {
                settings.WindowMaximized = WindowState == FormWindowState.Maximized;
                if (WindowState == FormWindowState.Normal)
                {
                    settings.WindowLocation = Location;
                    settings.WindowSize = Size;
                }
                else
                {
                    settings.WindowLocation = RestoreBounds.Location;
                    settings.WindowSize = RestoreBounds.Size;
                }

                settings.GameDir = txtGameDir.Text;
                settings.UnpackDir = txtUnpackDir.Text;
                settings.RepackDir = txtRepackDir.Text;
                settings.TabSelected = tclMain.SelectedIndex;
                settings.SplitterDistance = spcLogs.SplitterDistance;
            }
        }

        private void btnGameBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select your game install directory";
            folderBrowserDialog1.RootFolder = Environment.SpecialFolder.MyComputer;
            try
            {
                folderBrowserDialog1.SelectedPath = Path.GetFullPath(txtGameDir.Text);
            }
            catch (ArgumentException)
            {
                folderBrowserDialog1.SelectedPath = "";
            }
            folderBrowserDialog1.SelectedPath = txtGameDir.Text;
            folderBrowserDialog1.ShowNewFolderButton = false;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtGameDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnGameExplore_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtGameDir.Text))
                Process.Start(Path.GetFullPath(txtGameDir.Text));
            else
                SystemSounds.Hand.Play();
        }

        private void btnUnpackBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the directory to unpack textures into";
            folderBrowserDialog1.RootFolder = Environment.SpecialFolder.MyComputer;
            try
            {
                folderBrowserDialog1.SelectedPath = Path.GetFullPath(txtUnpackDir.Text);
            }
            catch (ArgumentException)
            {
                folderBrowserDialog1.SelectedPath = "";
            }
            folderBrowserDialog1.ShowNewFolderButton = true;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtUnpackDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnUnpackExplore_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtUnpackDir.Text))
                Process.Start(Path.GetFullPath(txtUnpackDir.Text));
            else
                SystemSounds.Hand.Play();
        }

        private void btnUnpack_Click(object sender, EventArgs e)
        {
            string unpackDir;
            try
            {
                unpackDir = Path.GetFullPath(txtUnpackDir.Text);
            }
            catch (ArgumentException)
            {
                MessageBox.Show("Invalid output path:\n" + txtUnpackDir.Text,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!Directory.Exists(txtGameDir.Text))
            {
                MessageBox.Show("Game directory not found:\n" + txtGameDir.Text,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                DialogResult result = DialogResult.OK;
                if (Directory.Exists(txtUnpackDir.Text))
                {
                    result = MessageBox.Show("The contents of this directory will be deleted:\n" + unpackDir,
                        "Warning!", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
                }

                if (result == DialogResult.OK)
                {
                    bool proceed = true;

                    try
                    {
                        if (Directory.Exists(unpackDir))
                            Directory.Delete(unpackDir, true);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        MessageBox.Show("Unpack directory could not be deleted. Try running as Administrator.\n"
                            + "Reason: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        proceed = false;
                    }

                    try
                    {
                        if (proceed)
                        {
                            Directory.CreateDirectory(unpackDir);
                            File.WriteAllText(unpackDir + "\\tpup_test.txt",
                                "Test file to see if TPUP can write to this directory.");
                            File.Delete(unpackDir + "\\tpup_test.txt");
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        MessageBox.Show("Unpack directory could not be written to. Try running as Administrator.\n"
                            + "Reason: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        proceed = false;
                    }

                    if (proceed)
                    {
                        enableControls(false);
                        txtLog.Clear();
                        logLength = 0;
                        errorLength = 0;
                        pbrProgress.Value = 0;
                        pbrProgress.Maximum = 0;
                        tpup = new TPUP(txtGameDir.Text, unpackDir, false, Environment.ProcessorCount);
                        tpupThread = new Thread(tpup.Start);
                        tpupThread.Start();
                    }
                }
            }
        }

        private void btnRepackBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the directory to load texture overrides from";
            folderBrowserDialog1.RootFolder = Environment.SpecialFolder.MyComputer;
            try
            {
                folderBrowserDialog1.SelectedPath = Path.GetFullPath(txtRepackDir.Text);
            }
            catch (ArgumentException)
            {
                folderBrowserDialog1.SelectedPath = "";
            }
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtRepackDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnRepackExplore_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtRepackDir.Text))
                Process.Start(Path.GetFullPath(txtRepackDir.Text));
            else
                SystemSounds.Hand.Play();
        }

        private void btnRepack_Click(object sender, EventArgs e)
        {
            string gameDir = txtGameDir.Text;
            string repackDir = txtRepackDir.Text;

            if (!Directory.Exists(gameDir))
            {
                MessageBox.Show("Game directory not found:\n" + gameDir,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (!Directory.Exists(repackDir))
            {
                MessageBox.Show("Override directory not found:\n" + repackDir,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                bool proceed = true;
                try
                {
                    File.WriteAllText(repackDir + "\\tpup_test.txt",
                        "Test file to see if TPUP can write to this directory.");
                    File.Delete(repackDir + "\\tpup_test.txt");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show("Repack directory could not be written to. Try running as Administrator.\n"
                            + "Reason: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    proceed = false;
                }

                if (proceed)
                {
                    enableControls(false);
                    txtLog.Clear();
                    logLength = 0;
                    errorLength = 0;
                    pbrProgress.Value = 0;
                    pbrProgress.Maximum = 0;
                    tpup = new TPUP(gameDir, repackDir, true, Environment.ProcessorCount);
                    tpupThread = new Thread(tpup.Start);
                    tpupThread.Start();
                }
            }
        }

        private void btnAbort_Click(object sender, EventArgs e)
        {
            tpup.Stop();
            btnAbort.Enabled = false;
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(txtGameDir.Text))
            {
                MessageBox.Show("Game directory not found:\n" + txtGameDir.Text,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                txtLog.Clear();
                int found = 0;
                foreach (string filepath in Directory.GetFiles(txtGameDir.Text, "*.tpupbak", SearchOption.AllDirectories))
                {
                    string newPath = Path.GetDirectoryName(filepath) + "\\" + Path.GetFileNameWithoutExtension(filepath);
                    if (File.Exists(newPath))
                        File.Delete(newPath);
                    File.Move(filepath, newPath);
                    found++;
                }

                if (found > 0)
                    appendLog(found + " backups restored.");
                else
                    appendLog("No backups found.");
            }
        }

        private void tmrCheckThread_Tick(object sender, EventArgs e)
        {
            if (tpupThread != null)
            {
                int newLogLength = tpup.GetLogLength();
                if (newLogLength > logLength)
                {
                    for (int i = logLength; i < newLogLength; i++)
                        appendLog(tpup.GetLogLine(i));
                    logLength = newLogLength;
                }

                int newErrorLength = tpup.GetErrorLength();
                if (newErrorLength > errorLength)
                {
                    for (int i = errorLength; i < newErrorLength; i++)
                        appendError(tpup.GetErrorLine(i));
                    errorLength = newErrorLength;
                }

                if (pbrProgress.Maximum == 0)
                    pbrProgress.Maximum = tpup.GetProgressMax();
                else
                    pbrProgress.Value = tpup.GetProgress();


                if (!tpupThread.IsAlive)
                {
                    tpup = null;
                    tpupThread = null;
                    pbrProgress.Maximum = 0;
                    pbrProgress.Value = 0;
                    enableControls(true);

                    if (abort)
                        Close();
                    else
                        SystemSounds.Asterisk.Play();
                }
            }
        }

        private void enableControls(bool enable)
        {
            txtGameDir.Enabled = enable;
            btnGameBrowse.Enabled = enable;
            tclMain.Enabled = enable;
            btnAbort.Enabled = !enable;
            btnRestore.Enabled = enable;
        }

        private void appendLog(string line)
        {
            if (txtLog.TextLength > 0)
                txtLog.AppendText("\r\n" + line);
            else
                txtLog.AppendText(line);
        }

        private void appendError((bool, string) line)
        {
            if (txtError.TextLength > 0)
                txtError.AppendText("\r\n" + line.Item2);
            else
                txtError.AppendText(line.Item2);
        }
    }
}
