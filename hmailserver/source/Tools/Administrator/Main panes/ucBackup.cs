// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.IO;
using System.Windows.Forms;
using hMailServer.Administrator.Utilities;
using System.Runtime.InteropServices;
using hMailServer.Shared;

namespace hMailServer.Administrator
{
    public partial class ucBackup : UserControl, ISettingsControl
    {
        private hMailServer.Backup _backup;

        // The passphrase cannot be read back, only whether there is one.
        private bool _passphraseIsSet;

        public ucBackup()
        {
            InitializeComponent();

            DirtyChecker.SubscribeToChange(this, OnContentChanged);

            new TabOrderManager(this).SetTabOrder(TabOrderManager.TabScheme.AcrossFirst);

            _backup = null;
        }

        public void OnLeavePage()
        {

        }

        private void OnContentChanged()
        {
            Instances.MainForm.OnContentChanged();
        }

        private void OnContentChanged(object sender, EventArgs e)
        {
            OnContentChanged();
        }

        public bool Dirty
        {
            get
            {
                return DirtyChecker.IsDirty(this);
            }
        }

        public void LoadData()
        {
            hMailServer.Settings settings = APICreator.Application.Settings;
            hMailServer.BackupSettings backupSettings = settings.Backup;

            textDestination.Text = backupSettings.Destination;
            checkBackupSettings.Checked = backupSettings.BackupSettings;
            checkBackupDomains.Checked = backupSettings.BackupDomains;
            checkBackupMessages.Checked = backupSettings.BackupMessages;
            checkCompressFiles.Checked = backupSettings.CompressDestinationFiles;

            _passphraseIsSet = backupSettings.PassphraseIsSet;
            checkEncrypt.Checked = _passphraseIsSet;
            textPassphrase.Text = "";
            textPassphraseConfirm.Text = "";
            EnablePassphraseFields();

            Marshal.ReleaseComObject(settings);
            Marshal.ReleaseComObject(backupSettings);
        }

        public bool SaveData()
        {
            if (checkEncrypt.Checked)
            {
                if (textPassphrase.Text != textPassphraseConfirm.Text)
                {
                    MessageBox.Show(Strings.Localize("The passphrases do not match."), EnumStrings.hMailServerAdministrator);
                    return false;
                }

                // Empty keeps the passphrase there is, but there must be one.
                if (textPassphrase.Text.Length == 0 && !_passphraseIsSet)
                {
                    MessageBox.Show(Strings.Localize("Enter the passphrase to encrypt backups with."), EnumStrings.hMailServerAdministrator);
                    return false;
                }
            }

            hMailServer.Settings settings = APICreator.Application.Settings;
            hMailServer.BackupSettings backupSettings = settings.Backup;

            try
            {
                if (!checkEncrypt.Checked)
                {
                    if (_passphraseIsSet)
                        backupSettings.Passphrase = "";
                }
                else if (textPassphrase.Text.Length > 0)
                {
                    backupSettings.Passphrase = textPassphrase.Text;
                }
            }
            catch (COMException ex)
            {
                // Too short.
                MessageBox.Show(ex.Message, EnumStrings.hMailServerAdministrator);

                Marshal.ReleaseComObject(settings);
                Marshal.ReleaseComObject(backupSettings);
                return false;
            }

            _passphraseIsSet = checkEncrypt.Checked;
            textPassphrase.Text = "";
            textPassphraseConfirm.Text = "";

            backupSettings.Destination = textDestination.Text;
            backupSettings.BackupSettings = checkBackupSettings.Checked;
            backupSettings.BackupDomains = checkBackupDomains.Checked;
            backupSettings.BackupMessages = checkBackupMessages.Checked;
            backupSettings.CompressDestinationFiles = checkCompressFiles.Checked;

            DirtyChecker.SetClean(this);

            Marshal.ReleaseComObject(settings);
            Marshal.ReleaseComObject(backupSettings);

            return true;
        }

        public void LoadResources()
        {
            // load the translated resources
        }

        private void buttonStartBackup_Click(object sender, EventArgs e)
        {
            // Save settings before starting the backup, so that
            // we actually use the settings the user sees on the
            // display.
            if (!SaveData())
                return;

            APICreator.Application.BackupManager.StartBackup();
        }

        private void checkEncrypt_CheckedChanged(object sender, EventArgs e)
        {
            EnablePassphraseFields();
        }

        private void EnablePassphraseFields()
        {
            textPassphrase.Enabled = checkEncrypt.Checked;
            textPassphraseConfirm.Enabled = checkEncrypt.Checked;
        }

        private void buttonShowLog_Click(object sender, EventArgs e)
        {
            ShowLogFile();
        }

        private static void ShowLogFile()
        {
            hMailServer.BackupSettings backupSettings = APICreator.Application.Settings.Backup;

            string logFileName = backupSettings.LogFile;

            try
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.FileName = logFileName;
                process.StartInfo.UseShellExecute = true;
                process.Start();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, EnumStrings.hMailServerAdministrator);
            }
        }

        private void buttonSelectBackupFile_Click(object sender, EventArgs e)
        {
            openFileDialog.Title = Strings.Localize("Select backup file");

            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            using (new WaitCursor())
            {
               string fileName = openFileDialog.FileName;

               textRestoreFile.Text = fileName;
            }
        }

       private void EnableRestoreSelection(string fileName)
       {
          try
          {
             // Without its passphrase an encrypted backup reads as one with nothing in it.
             string passphrase = textRestorePassphrase.Text;
             _backup = passphrase.Length == 0
                ? APICreator.Application.BackupManager.LoadBackup(fileName)
                : APICreator.Application.BackupManager.LoadEncryptedBackup(fileName, passphrase);

             checkRestoreDomains.Checked = false;
             checkRestoreMessages.Checked = false;
             checkRestoreSettings.Checked = false;

             checkRestoreSettings.Enabled = _backup.ContainsSettings;
             checkRestoreDomains.Enabled = _backup.ContainsDomains;
             checkRestoreMessages.Enabled = _backup.ContainsMessages;

             // Is there anything to restore in the backup?
             buttonStartRestore.Enabled = checkRestoreSettings.Enabled ||
                                          checkRestoreDomains.Enabled ||
                                          checkRestoreMessages.Enabled;
          }
          catch (Exception ex)
          {
             _backup = null;
             buttonStartRestore.Enabled = false;
             MessageBox.Show(ex.Message, EnumStrings.hMailServerAdministrator);
          }

       }

       private void buttonOpenBackup_Click(object sender, EventArgs e)
       {
          if (!File.Exists(textRestoreFile.Text))
          {
             MessageBox.Show(Strings.Localize("Select the backup file first."), EnumStrings.hMailServerAdministrator);
             return;
          }

          using (new WaitCursor())
          {
             EnableRestoreSelection(textRestoreFile.Text);
          }
       }

       private void buttonShowLog2_Click(object sender, EventArgs e)
        {
            ShowLogFile();
        }

        private void buttonStartRestore_Click(object sender, EventArgs e)
        {
            string message = "WARNING! All settings will be cleared & ALL MESSAGES DELETED prior to restore. Are you sure?";

            if (MessageBox.Show(Strings.Localize(message), EnumStrings.hMailServerAdministrator, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                if (_backup != null)
                {
                    if (checkRestoreSettings.Enabled ||
                        checkRestoreDomains.Enabled ||
                        checkRestoreMessages.Enabled)
                    {
                        _backup.RestoreDomains = checkRestoreDomains.Checked;
                        _backup.RestoreMessages = checkRestoreMessages.Checked;
                        _backup.RestoreSettings = checkRestoreSettings.Checked;

                        _backup.StartRestore();
                    }
                }
            }
        }

        private void buttonSelectCertificate_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.SelectedPath = textDestination.Text;
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                textDestination.Text = folderBrowserDialog.SelectedPath;

        }

        private void textRestoreFile_TextChanged(object sender, EventArgs e)
        {
           if (File.Exists(textRestoreFile.Text))
           {
              EnableRestoreSelection(textRestoreFile.Text);
           }
           else
           {
              checkRestoreDomains.Checked = false;
              checkRestoreMessages.Checked = false;
              checkRestoreSettings.Checked = false;
              checkRestoreDomains.Enabled = false;
              checkRestoreMessages.Enabled = false;
              checkRestoreSettings.Enabled = false;
              buttonStartRestore.Enabled = false;
           }
        }

    }
}
