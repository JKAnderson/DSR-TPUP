using Octokit;
using Semver;
using System;
using System.Diagnostics;
using System.IO;
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
        private int logLength;
        private bool abort = false;

        public FormMain()
        {
            InitializeComponent();
        }

        private async void FormMain_Load(object sender, EventArgs e)
        {
            Text = "DSR Texture Packer & Unpacker " + System.Windows.Forms.Application.ProductVersion;
            Location = settings.WindowLocation;
            txtGameDir.Text = settings.GameDir;
            txtUnpackDir.Text = settings.UnpackDir;
            txtRepackDir.Text = settings.RepackDir;
            tclMain.SelectedIndex = settings.TabSelected;
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
                appendLog("Stopping...");
                tpup.Stop = true;
                e.Cancel = true;
                abort = true;
            }
            else
            {
                if (WindowState == FormWindowState.Normal)
                    settings.WindowLocation = Location;
                else
                    settings.WindowLocation = RestoreBounds.Location;
                settings.GameDir = txtGameDir.Text;
                settings.UnpackDir = txtUnpackDir.Text;
                settings.RepackDir = txtRepackDir.Text;
                settings.TabSelected = tclMain.SelectedIndex;
            }
        }

        private void btnGameBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select your game install directory";
            folderBrowserDialog1.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog1.SelectedPath = txtGameDir.Text;
            folderBrowserDialog1.ShowNewFolderButton = false;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtGameDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnUnpackBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the directory to unpack textures into";
            folderBrowserDialog1.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog1.SelectedPath = txtUnpackDir.Text;
            folderBrowserDialog1.ShowNewFolderButton = true;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtUnpackDir.Text = folderBrowserDialog1.SelectedPath;
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
                    if (Directory.Exists(unpackDir))
                        Directory.Delete(unpackDir, true);
                    enableControls(false);
                    txtLog.Clear();
                    appendLog("Unpacking all textures...");
                    tpup = new TPUP(Environment.ProcessorCount);
                    tpupThread = new Thread(() => tpup.ProcessFile(txtGameDir.Text, unpackDir, false));
                    tpupThread.Start();
                }
            }
        }

        private void btnRepackBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.Description = "Select the directory to load texture overrides from";
            folderBrowserDialog1.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog1.SelectedPath = txtRepackDir.Text;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                txtUnpackDir.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnRepack_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(txtGameDir.Text))
            {
                MessageBox.Show("Game directory not found:\n" + txtGameDir.Text,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (!Directory.Exists(txtRepackDir.Text))
            {
                MessageBox.Show("Override directory not found:\n" + txtRepackDir.Text,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                enableControls(false);
                txtLog.Clear();
                appendLog("Repacking overridden textures...");
                tpup = new TPUP(Environment.ProcessorCount);
                tpupThread = new Thread(() => tpup.ProcessFile(txtGameDir.Text, txtRepackDir.Text, true));
                tpupThread.Start();
            }
        }

        private void btnAbort_Click(object sender, EventArgs e)
        {
            tpup.Stop = true;
            appendLog("Stopping...");
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

                if (!tpupThread.IsAlive)
                {
                    tpup = null;
                    tpupThread = null;
                    logLength = 0;
                    enableControls(true);

                    if (abort)
                        Close();
                    else
                        appendLog("Finished!");
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
    }
}
