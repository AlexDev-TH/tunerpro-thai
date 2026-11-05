// ตัวติดตั้งภาษาไทยของ TunerPro RT v6
//
// TunerPro อ่านข้อความจาก lang\en.txt เป็นไบต์ดิบแล้วแสดงผลตามภาษาของเครื่อง
// ตัวโปรแกรมและ DLL ทุกตัวอ่านไฟล์นี้ จึงต้องเขียนภาษาไทยลงไฟล์นี้โดยตรงเป็น Windows-874
// ไฟล์เดิมจะถูกเก็บไว้ที่ lang\en.txt.original เพื่อคืนค่าได้
// ตัวติดตั้งไม่แก้ไฟล์ .exe หรือ .dll ของ TunerPro

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("TunerPro Thai Setup")]
[assembly: AssemblyProduct("TunerPro Thai")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace TunerProThai
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "/install") return AdminTasks.Install(args);
            if (args.Length >= 2 && args[0] == "/uninstall") return AdminTasks.Uninstall(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(args.Length == 1 ? args[0] : null));
            return 0;
        }
    }

    static class ExitCode
    {
        public const int Ok = 0;
        public const int FileFailed = 2;
        public const int LocaleFailed = 3;
        public const int Cancelled = -1;
    }

    static class Core
    {
        const string MarkerLine = "# TunerPro-Thai";
        const int ThaiCodePage = 874;
        const string ThaiLocale = "th-TH";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TunerPro RT v6_is1";

        [DllImport("kernel32.dll")] static extern uint GetACP();
        [DllImport("kernel32.dll")] static extern uint GetSystemDefaultLCID();

        // ใช้ map ไบต์ต่อไบต์ เพื่อไม่ให้ค่าอื่นในไฟล์เสีย ไม่ว่าจะเข้ารหัสแบบไหน
        static readonly Encoding Bytes = Encoding.GetEncoding(28591);

        public static bool IsThaiCodePage()
        {
#if TESTHOOKS
            string fake = Environment.GetEnvironmentVariable("TPTHAI_TEST_ACP");
            if (!string.IsNullOrEmpty(fake)) return int.Parse(fake) == ThaiCodePage;
#endif
            return GetACP() == ThaiCodePage;
        }

        public static string EnglishFile(string dir) { return Path.Combine(dir, "lang", "en.txt"); }
        static string BackupFile(string dir) { return EnglishFile(dir) + ".original"; }

        public static bool IsValidFolder(string dir)
        {
            return !string.IsNullOrEmpty(dir)
                && File.Exists(Path.Combine(dir, "TunerPro.exe"))
                && File.Exists(EnglishFile(dir));
        }

        public static string FindTunerPro()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = root.OpenSubKey(UninstallKey))
                        {
                            string location = key == null ? null : key.GetValue("InstallLocation") as string;
                            if (IsValidFolder(location)) return location.TrimEnd('\\');
                        }
                    }
                    catch (Exception) { }
                }
            }
            foreach (Environment.SpecialFolder root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            {
                string guess = Path.Combine(Environment.GetFolderPath(root), "TunerPro RT v6");
                if (IsValidFolder(guess)) return guess;
            }
            return null;
        }

        public static bool IsRunning()
        {
            return Process.GetProcessesByName("TunerPro").Length > 0;
        }

        public static bool IsInstalled(string dir)
        {
            return HasMarker(File.ReadAllBytes(EnglishFile(dir)));
        }

        public static bool CanWrite(string dir)
        {
            string probe = Path.Combine(dir, "lang", ".tpthai-write-test");
            try
            {
                File.WriteAllBytes(probe, new byte[0]);
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool HasMarker(byte[] data)
        {
            string head = Bytes.GetString(data, 0, Math.Min(data.Length, MarkerLine.Length));
            return head == MarkerLine;
        }

        static Dictionary<string, byte[]> LoadTranslation()
        {
            Encoding thai = Encoding.GetEncoding(ThaiCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            Dictionary<string, byte[]> map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("th.txt"))
            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    map[line.Substring(0, eq).Trim()] = thai.GetBytes(line.Substring(eq + 1).Trim());
                }
            }
            return map;
        }

        // แทนเฉพาะค่าของคีย์ที่แปลแล้ว คีย์ใหม่ที่ TunerPro เพิ่มมาภายหลังจะยังเป็นภาษาอังกฤษ
        static byte[] Merge(byte[] english, Dictionary<string, byte[]> thai)
        {
            using (MemoryStream output = new MemoryStream(english.Length * 2))
            {
                Write(output, MarkerLine + "\n");
                int pos = 0;
                while (pos < english.Length)
                {
                    int newline = Array.IndexOf(english, (byte)'\n', pos);
                    int end = newline < 0 ? english.Length : newline;
                    string line = Bytes.GetString(english, pos, end - pos).TrimEnd('\r');
                    pos = end + 1;

                    string trimmed = line.TrimStart();
                    int eq = line.IndexOf('=');
                    if (trimmed.Length > 0 && trimmed[0] != '#' && eq > 0)
                    {
                        string key = line.Substring(0, eq).Trim();
                        byte[] value;
                        if (thai.TryGetValue(key, out value))
                        {
                            Write(output, key + " = ");
                            output.Write(value, 0, value.Length);
                            Write(output, "\n");
                            continue;
                        }
                    }
                    Write(output, line + "\n");
                }
                return output.ToArray();
            }
        }

        static void Write(Stream output, string text)
        {
            byte[] data = Bytes.GetBytes(text);
            output.Write(data, 0, data.Length);
        }

        static void WriteChecked(string path, byte[] data)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Copy(temp, path, true);
            File.Delete(temp);
            byte[] back = File.ReadAllBytes(path);
            if (back.Length != data.Length) throw new IOException("Written file size does not match.");
            for (int i = 0; i < data.Length; i++)
            {
                if (back[i] != data[i]) throw new IOException("Written file content does not match.");
            }
        }

        public static void Install(string dir)
        {
            string en = EnglishFile(dir);
            string backup = BackupFile(dir);
            byte[] current = File.ReadAllBytes(en);
            byte[] source;
            if (!HasMarker(current))
            {
                // เป็นไฟล์ของ TunerPro จริง เช่น ติดตั้งครั้งแรก หรือเพิ่งอัปเดต TunerPro มา
                File.Copy(en, backup, true);
                source = current;
            }
            else if (File.Exists(backup))
            {
                source = File.ReadAllBytes(backup);
            }
            else
            {
                source = current;
            }
            WriteChecked(en, Merge(source, LoadTranslation()));
        }

        public static void Uninstall(string dir)
        {
            string en = EnglishFile(dir);
            string backup = BackupFile(dir);
            if (!HasMarker(File.ReadAllBytes(en)))
            {
                if (File.Exists(backup)) File.Delete(backup);
                return;
            }
            if (!File.Exists(backup)) throw new FileNotFoundException("Backup is missing.", backup);
            WriteChecked(en, File.ReadAllBytes(backup));
            File.Delete(backup);
        }

        // ภาษาที่เลือกไว้ต้องเป็น en เพราะภาษาไทยอยู่ใน en.txt
        public static void EnsureEnglishLanguageSetting()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TunerPro Files", "TunerProSettings.json");
            if (!File.Exists(path)) return;
            string text = Bytes.GetString(File.ReadAllBytes(path));
            Match match = Regex.Match(text, "\"language\"\\s*:\\s*\"([^\"]*)\"");
            if (!match.Success || match.Groups[1].Value == "en") return;
            string backup = path + ".bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            string updated = text.Substring(0, match.Index) + "\"language\": \"en\"" + text.Substring(match.Index + match.Length);
            File.WriteAllBytes(path, Bytes.GetBytes(updated));
        }

        static string LocaleMarkerFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TunerPro-Thai", "previous-locale.txt");
        }

        public static string PreviousLocale()
        {
            try
            {
                string path = LocaleMarkerFile();
                if (!File.Exists(path)) return null;
                string value = File.ReadAllText(path).Trim();
                return IsLocaleName(value) ? value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool IsLocaleName(string value)
        {
            return Regex.IsMatch(value, "^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$");
        }

        public static void SetThaiLocale()
        {
            string marker = LocaleMarkerFile();
            bool createdMarker = false;
            if (!File.Exists(marker))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                string current;
                try { current = CultureInfo.GetCultureInfo((int)GetSystemDefaultLCID()).Name; }
                catch (Exception) { current = "en-US"; }
                File.WriteAllText(marker, current);
                createdMarker = true;
            }
            if (!RunSetLocale(ThaiLocale))
            {
                if (createdMarker) File.Delete(marker);
                throw new InvalidOperationException("Could not change the system locale.");
            }
        }

        public static void RestoreLocale()
        {
            string value = PreviousLocale();
            if (value == null) return;
            if (!RunSetLocale(value)) throw new InvalidOperationException("Could not change the system locale.");
            string marker = LocaleMarkerFile();
            File.Delete(marker);
            string folder = Path.GetDirectoryName(marker);
            if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
        }

        static bool RunSetLocale(string locale)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            info.Arguments = "-NoProfile -NonInteractive -Command \"try { Set-WinSystemLocale -SystemLocale '" + locale + "' -ErrorAction Stop; exit 0 } catch { exit 1 }\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            using (Process process = Process.Start(info))
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
    }

    // ทำงานในโปรเซสที่ได้สิทธิ์ admin แยกจากหน้าต่างหลัก
    static class AdminTasks
    {
        public static int Install(string[] args)
        {
            string dir = args[1];
            bool locale = args.Length > 2 && args[2] == "/locale";
            try
            {
                if (!Core.IsValidFolder(dir)) return ExitCode.FileFailed;
                Core.Install(dir);
            }
            catch (Exception)
            {
                return ExitCode.FileFailed;
            }
            if (locale)
            {
                try { Core.SetThaiLocale(); }
                catch (Exception) { return ExitCode.LocaleFailed; }
            }
            return ExitCode.Ok;
        }

        public static int Uninstall(string[] args)
        {
            string dir = args[1];
            bool locale = args.Length > 2 && args[2] == "/restore-locale";
            try
            {
                if (!Core.IsValidFolder(dir)) return ExitCode.FileFailed;
                Core.Uninstall(dir);
            }
            catch (Exception)
            {
                return ExitCode.FileFailed;
            }
            if (locale)
            {
                try { Core.RestoreLocale(); }
                catch (Exception) { return ExitCode.LocaleFailed; }
            }
            return ExitCode.Ok;
        }
    }

    sealed class SetupForm : Form
    {
        const string ManualLocaleSteps =
            "ตั้งเองได้ที่ Control Panel แล้วเลือก Region จากนั้นแท็บ Administrative กด Change system locale เลือก Thai (Thailand) แล้วรีสตาร์ตเครื่อง";

        readonly TextBox txtFolder = new TextBox();
        readonly Button btnBrowse = new Button();
        readonly Button btnInstall = new Button();
        readonly Button btnRemove = new Button();
        readonly Button btnClose = new Button();
        readonly Label lblStatus = new Label();
        readonly Label lblLocale = new Label();
        readonly CheckBox chkLocale = new CheckBox();
        string folder;

        public SetupForm(string folderArgument)
        {
            folder = Core.IsValidFolder(folderArgument) ? folderArgument.TrimEnd('\\') : Core.FindTunerPro();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Tahoma", 9.75f);
            Text = "TunerPro ภาษาไทย";
            ClientSize = new Size(560, 326);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            Label title = new Label();
            title.Text = "ติดตั้งภาษาไทยให้ TunerPro RT v6";
            title.Font = new Font("Tahoma", 13f, FontStyle.Bold);
            title.SetBounds(16, 12, 528, 30);

            Label info = new Label();
            info.Text = "เปลี่ยนข้อความในไฟล์ภาษาของ TunerPro เป็นภาษาไทย เก็บไฟล์เดิมไว้ให้ และไม่ได้แก้ไฟล์ตัวโปรแกรม กลับเป็นภาษาอังกฤษได้ทุกเมื่อ";
            info.SetBounds(16, 46, 528, 42);

            Label lblFolder = new Label();
            lblFolder.Text = "โฟลเดอร์ TunerPro";
            lblFolder.SetBounds(16, 94, 528, 20);

            txtFolder.ReadOnly = true;
            txtFolder.TabStop = false;
            txtFolder.SetBounds(16, 117, 430, 24);

            btnBrowse.Text = "เลือก...";
            btnBrowse.SetBounds(454, 115, 90, 28);
            btnBrowse.Click += OnBrowse;

            lblStatus.Font = new Font("Tahoma", 9.75f, FontStyle.Bold);
            lblStatus.SetBounds(16, 152, 528, 24);

            lblLocale.ForeColor = Color.FromArgb(160, 60, 0);
            lblLocale.SetBounds(16, 180, 528, 42);

            chkLocale.Text = "ตั้งเป็นภาษาไทยให้เลย (ต้องรีสตาร์ตเครื่องหนึ่งครั้ง)";
            chkLocale.Checked = true;
            chkLocale.SetBounds(16, 226, 528, 26);

            btnInstall.Text = "ติดตั้งภาษาไทย";
            btnInstall.SetBounds(16, 272, 170, 38);
            btnInstall.Click += OnInstall;

            btnRemove.Text = "กลับเป็นภาษาอังกฤษ";
            btnRemove.SetBounds(194, 272, 170, 38);
            btnRemove.Click += OnRemove;

            btnClose.Text = "ปิด";
            btnClose.SetBounds(444, 272, 100, 38);
            btnClose.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { title, info, lblFolder, txtFolder, btnBrowse, lblStatus, lblLocale, chkLocale, btnInstall, btnRemove, btnClose });
            AcceptButton = btnInstall;
            CancelButton = btnClose;
            ActiveControl = btnInstall;
            ResumeLayout(false);
            PerformLayout();

            RefreshState();
        }

        void RefreshState()
        {
            bool valid = Core.IsValidFolder(folder);
            txtFolder.Text = folder ?? "";
            btnInstall.Enabled = valid;

            bool installed = false;
            if (valid)
            {
                try { installed = Core.IsInstalled(folder); }
                catch (Exception) { }
            }
            btnRemove.Enabled = installed;

            if (!valid)
            {
                lblStatus.ForeColor = Color.Firebrick;
                lblStatus.Text = "ไม่พบ TunerPro RT v6 กด เลือก... เพื่อชี้โฟลเดอร์เอง";
            }
            else if (installed)
            {
                lblStatus.ForeColor = Color.ForestGreen;
                lblStatus.Text = "สถานะ: ติดตั้งภาษาไทยแล้ว";
            }
            else
            {
                lblStatus.ForeColor = SystemColors.ControlText;
                lblStatus.Text = "สถานะ: ยังไม่ได้ติดตั้งภาษาไทย";
            }

            bool thai = Core.IsThaiCodePage();
            bool pending = !thai && Core.PreviousLocale() != null;
            lblLocale.Visible = !thai;
            chkLocale.Visible = !thai && !pending;
            lblLocale.Text = pending
                ? "ตั้งภาษาของเครื่องเป็นไทยแล้ว รอรีสตาร์ตเครื่องหนึ่งครั้ง ภาษาไทยใน TunerPro จะแสดงถูกต้องหลังจากนั้น"
                : "เครื่องนี้ยังไม่ได้ตั้งภาษาสำหรับโปรแกรมที่ไม่ใช่ Unicode เป็นไทย ถ้าไม่ตั้ง TunerPro จะแสดงเป็นตัวอ่านไม่ออก";
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "เลือกโฟลเดอร์ที่มีไฟล์ TunerPro.exe";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!Core.IsValidFolder(dialog.SelectedPath))
                {
                    Warn("โฟลเดอร์นี้ไม่มี TunerPro.exe กับไฟล์ lang\\en.txt\n\nโปรแกรมนี้ใช้ได้กับ TunerPro RT v6 เท่านั้น");
                    return;
                }
                folder = dialog.SelectedPath.TrimEnd('\\');
                RefreshState();
            }
        }

        bool TunerProIsClosed()
        {
            if (!Core.IsRunning()) return true;
            Warn("TunerPro เปิดอยู่\n\nบันทึกงานแล้วปิดโปรแกรมก่อน แล้วกดอีกครั้ง");
            return false;
        }

        void OnInstall(object sender, EventArgs e)
        {
            if (!TunerProIsClosed()) return;

            bool thai = Core.IsThaiCodePage();
            bool pending = !thai && Core.PreviousLocale() != null;
            bool setLocale = !thai && !pending && chkLocale.Checked;
            if (!thai && !pending && !setLocale)
            {
                if (!Ask("ถ้าไม่ตั้งภาษาของเครื่องเป็นไทย TunerPro จะแสดงเป็นตัวอ่านไม่ออก\n\nติดตั้งต่อไหม?")) return;
            }

            int code;
            if (!setLocale && Core.CanWrite(folder))
            {
                try { Core.Install(folder); code = ExitCode.Ok; }
                catch (Exception) { code = ExitCode.FileFailed; }
            }
            else
            {
                code = RunAsAdmin("/install \"" + folder + "\"" + (setLocale ? " /locale" : ""));
            }

            if (code == ExitCode.Ok || code == ExitCode.LocaleFailed)
            {
                try { Core.EnsureEnglishLanguageSetting(); }
                catch (Exception) { }
            }
            RefreshState();

            if (code == ExitCode.Cancelled)
            {
                Warn("ยกเลิกแล้ว ยังไม่ได้เปลี่ยนอะไร\n\nต้องกด Yes ที่หน้าต่างขออนุญาตของ Windows ถึงจะติดตั้งได้");
            }
            else if (code == ExitCode.FileFailed)
            {
                Warn("เขียนไฟล์ภาษาไม่สำเร็จ ลองปิดโปรแกรมอื่นที่อาจเปิดไฟล์นี้อยู่แล้วลองใหม่");
            }
            else if (code == ExitCode.LocaleFailed)
            {
                Warn("ติดตั้งไฟล์ภาษาแล้ว แต่ตั้งภาษาของเครื่องไม่สำเร็จ\n\n" + ManualLocaleSteps);
            }
            else if (setLocale || pending)
            {
                AskRestart("ติดตั้งเสร็จแล้ว แต่ต้องรีสตาร์ตเครื่องก่อน ภาษาไทยใน TunerPro ถึงจะแสดงถูกต้อง");
            }
            else if (Ask("ติดตั้งเสร็จแล้ว\n\nเปิด TunerPro เลยไหม?"))
            {
                ProcessStartInfo info = new ProcessStartInfo(Path.Combine(folder, "TunerPro.exe"));
                info.WorkingDirectory = folder;
                Process.Start(info);
            }
        }

        void OnRemove(object sender, EventArgs e)
        {
            if (!TunerProIsClosed()) return;
            if (!Ask("คืนไฟล์ภาษาอังกฤษเดิมของ TunerPro ใช่ไหม?")) return;

            bool restoreLocale = false;
            string previous = Core.PreviousLocale();
            if (previous != null)
            {
                restoreLocale = Ask("ตอนติดตั้ง โปรแกรมนี้เปลี่ยนภาษาของเครื่องจาก " + previous + " เป็นไทย\n\nเปลี่ยนกลับเป็น " + previous + " ด้วยไหม? (ต้องรีสตาร์ตเครื่อง)");
            }

            int code;
            if (!restoreLocale && Core.CanWrite(folder))
            {
                try { Core.Uninstall(folder); code = ExitCode.Ok; }
                catch (Exception) { code = ExitCode.FileFailed; }
            }
            else
            {
                code = RunAsAdmin("/uninstall \"" + folder + "\"" + (restoreLocale ? " /restore-locale" : ""));
            }
            RefreshState();

            if (code == ExitCode.Cancelled)
            {
                Warn("ยกเลิกแล้ว ยังไม่ได้เปลี่ยนอะไร");
            }
            else if (code == ExitCode.FileFailed)
            {
                Warn("คืนไฟล์ไม่สำเร็จ ถ้ายังไม่หายให้ติดตั้ง TunerPro ทับอีกครั้ง ไฟล์ภาษาอังกฤษจะกลับมาเอง");
            }
            else if (code == ExitCode.LocaleFailed)
            {
                Warn("คืนไฟล์ภาษาแล้ว แต่เปลี่ยนภาษาของเครื่องกลับไม่สำเร็จ\n\nตั้งเองได้ที่ Control Panel แล้วเลือก Region จากนั้นแท็บ Administrative กด Change system locale");
            }
            else if (restoreLocale)
            {
                AskRestart("กลับเป็นภาษาอังกฤษแล้ว ต้องรีสตาร์ตเครื่องเพื่อให้ภาษาของเครื่องกลับเหมือนเดิม");
            }
            else
            {
                MessageBox.Show(this, "กลับเป็นภาษาอังกฤษแล้ว", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        int RunAsAdmin(string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, arguments);
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.ErrorDialogParentHandle = Handle;
            Enabled = false;
            try
            {
                using (Process process = Process.Start(info))
                {
                    while (!process.WaitForExit(100)) Application.DoEvents();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception error)
            {
                return error.NativeErrorCode == 1223 ? ExitCode.Cancelled : ExitCode.FileFailed;
            }
            finally
            {
                Enabled = true;
            }
        }

        void AskRestart(string message)
        {
            if (!Ask(message + "\n\nรีสตาร์ตตอนนี้เลยไหม? (บันทึกงานที่เปิดค้างไว้ก่อน)")) return;
            ProcessStartInfo info = new ProcessStartInfo("shutdown.exe", "/r /t 5");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            Process.Start(info);
        }

        bool Ask(string message)
        {
            return MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        void Warn(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
