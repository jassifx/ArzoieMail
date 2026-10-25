// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using hMailServer.Administrator.Utilities;

namespace hMailServer.Administrator
{
   // The installation's secret key, under which the server protects the secrets it reads back.
   // Exported under a passphrase it moves to another computer - a server moved, or a second
   // server sharing the database - and imported there it becomes that installation's key.
   public partial class ucSecretKey : UserControl, ISettingsControl
   {
      private const int MinimumPassphraseLength = 12;

      private readonly Label _keyId = new Label();

      public ucSecretKey()
      {
         var layout = new TableLayoutPanel
         {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(10)
         };

         var explanation = new Label
         {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = "The server protects the secrets it has to read back - the database password, the passwords of " +
                   "external accounts and routes, the relay password - under a key of its own, which only this " +
                   "computer can unlock.\r\n\r\n" +
                   "Export the key to move the server to another computer, or to give another server that shares " +
                   "this database the same key, and import it there. The export is encrypted under a passphrase: " +
                   "keep both safe, as anyone who has them can read the secrets."
         };

         var exportButton = new Button { Text = "Export key...", AutoSize = true };
         exportButton.Click += OnExport;

         var importButton = new Button { Text = "Import key...", AutoSize = true };
         importButton.Click += OnImport;

         var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
         buttons.Controls.Add(exportButton);
         buttons.Controls.Add(importButton);

         _keyId.AutoSize = true;
         _keyId.Margin = new Padding(3, 10, 3, 0);

         layout.Controls.Add(explanation);
         layout.Controls.Add(_keyId);
         layout.Controls.Add(buttons);

         Controls.Add(layout);
      }

      public void OnLeavePage()
      {
      }

      // Nothing here is saved with the settings: export and import act at once.
      public bool Dirty => false;

      public void LoadData()
      {
         var utilities = APICreator.Application.Utilities;
         string keyId = utilities.SecretKeyId;
         Marshal.ReleaseComObject(utilities);

         _keyId.Text = string.IsNullOrEmpty(keyId)
            ? "The installation has no secret key it can use. See the error log."
            : "Key id: " + keyId + "   (servers sharing a database need the same one)";
      }

      public bool SaveData()
      {
         return true;
      }

      public void LoadResources()
      {
      }

      private void OnExport(object sender, EventArgs e)
      {
         string passphrase = AskPassphrase("Export the secret key", confirm: true);
         if (passphrase == null)
            return;

         using (var dialog = new SaveFileDialog
         {
            Title = "Save the exported secret key",
            Filter = "Exported secret key (*.txt)|*.txt",
            FileName = "ArzoieMail secret key.txt"
         })
         {
            if (dialog.ShowDialog(this) != DialogResult.OK)
               return;

            try
            {
               var utilities = APICreator.Application.Utilities;
               string exported = utilities.ExportSecretKey(passphrase);
               Marshal.ReleaseComObject(utilities);

               File.WriteAllText(dialog.FileName, exported + Environment.NewLine);
               MessageBox.Show(this, "The secret key was exported to " + dialog.FileName + ".", EnumStrings.hMailServerAdministrator,
                  MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
               MessageBox.Show(this, ex.Message, EnumStrings.hMailServerAdministrator, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
         }
      }

      private void OnImport(object sender, EventArgs e)
      {
         using (var dialog = new OpenFileDialog
         {
            Title = "Open an exported secret key",
            Filter = "Exported secret key (*.txt)|*.txt|All files (*.*)|*.*"
         })
         {
            if (dialog.ShowDialog(this) != DialogResult.OK)
               return;

            string passphrase = AskPassphrase("Import the secret key", confirm: false);
            if (passphrase == null)
               return;

            try
            {
               string exported = File.ReadAllText(dialog.FileName).Trim();

               var utilities = APICreator.Application.Utilities;
               utilities.ImportSecretKey(exported, passphrase);
               Marshal.ReleaseComObject(utilities);

               LoadData();

               MessageBox.Show(this,
                  "The secret key was imported, and the server's secrets are protected under it.\r\n\r\n" +
                  "If the server could not connect to its database before, restart the ArzoieMail service.",
                  EnumStrings.hMailServerAdministrator, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
               MessageBox.Show(this, ex.Message, EnumStrings.hMailServerAdministrator, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
         }
      }

      // The passphrase, or null when the dialog is cancelled. With confirm, it is asked twice and
      // must be long enough.
      private string AskPassphrase(string title, bool confirm)
      {
         using (var form = new Form
         {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10)
         })
         {
            var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };

            var first = new TextBox { UseSystemPasswordChar = true, Width = 260 };
            var second = new TextBox { UseSystemPasswordChar = true, Width = 260 };

            layout.Controls.Add(new Label { Text = "Passphrase:", AutoSize = true, Anchor = AnchorStyles.Left });
            layout.Controls.Add(first);

            if (confirm)
            {
               layout.Controls.Add(new Label { Text = "Repeat it:", AutoSize = true, Anchor = AnchorStyles.Left });
               layout.Controls.Add(second);
            }

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);
            layout.SetColumnSpan(buttons, 2);

            form.Controls.Add(layout);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            while (form.ShowDialog(this) == DialogResult.OK)
            {
               if (!confirm)
                  return first.Text;

               if (first.Text.Length < MinimumPassphraseLength)
                  MessageBox.Show(form, $"The passphrase must have at least {MinimumPassphraseLength} characters.", title);
               else if (first.Text != second.Text)
                  MessageBox.Show(form, "The two passphrases are not the same.", title);
               else
                  return first.Text;
            }

            return null;
         }
      }
   }
}
