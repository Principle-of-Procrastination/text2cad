using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SwCopilot.UI;

namespace SwCopilot
{
    /// <summary>
    /// SolidWorks add-in entry point. Implements ISwAddin, registers the required
    /// SolidWorks registry keys on COM registration, and hosts the Task Pane UI.
    /// </summary>
    [ComVisible(true)]
    [Guid("D2F5E8A1-3B4C-4D5E-9F6A-7B8C9D0E1F2A")]
    public class SwAddin : ISwAddin
    {
        private const string Title = "SolidWorks Copilot";
        private const string Description = "SolidWorks Copilot MVP - chat-driven native feature automation";

        private ISldWorks _app;
        private int _cookie;
        private TaskpaneView _taskPaneView;
        private CopilotControl _control;

        // ---------- ISwAddin ----------

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _app = ThisSW as ISldWorks;
            _cookie = Cookie;
            if (_app == null) return false;

            try
            {
                string icon = CreateIconFile();
                _taskPaneView = _app.CreateTaskpaneView2(icon, Title);
                _control = new CopilotControl(_app);

                // Host the WinForms control by its window handle (x64 variant).
                _taskPaneView.DisplayWindowFromHandlex64(_control.Handle.ToInt64());
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("SolidWorks Copilot 加载失败:\n" + ex.Message,
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try { if (_taskPaneView != null) _taskPaneView.DeleteView(); }
            catch { }
            _taskPaneView = null;

            if (_control != null)
            {
                try { _control.Dispose(); } catch { }
                _control = null;
            }

            _app = null;
            // Release COM references promptly.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        // ---------- COM (un)registration ----------
        // regasm /codebase calls these; they add/remove the keys SolidWorks reads
        // at startup to discover the add-in.

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                string guid = "{" + t.GUID.ToString().ToUpperInvariant() + "}";

                using (RegistryKey addinKey =
                    Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\" + guid))
                {
                    addinKey.SetValue(null, 1, RegistryValueKind.DWord); // 1 = load the add-in
                    addinKey.SetValue("Title", Title, RegistryValueKind.String);
                    addinKey.SetValue("Description", Description, RegistryValueKind.String);
                }

                using (RegistryKey startupKey =
                    Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + guid))
                {
                    startupKey.SetValue(null, 1, RegistryValueKind.DWord); // 1 = load at startup for this user
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SwCopilot register error: " + ex.Message);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                string guid = "{" + t.GUID.ToString().ToUpperInvariant() + "}";
                Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\SolidWorks\Addins\" + guid, false);
                Registry.CurrentUser.DeleteSubKey(@"Software\SolidWorks\AddInsStartup\" + guid, false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SwCopilot unregister error: " + ex.Message);
            }
        }

        // ---------- helpers ----------

        /// <summary>Write a small tab bitmap to %TEMP% for CreateTaskpaneView2.</summary>
        private static string CreateIconFile()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "swcopilot_icon.bmp");
                using (Bitmap bmp = new Bitmap(16, 16))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(0, 120, 215));
                    using (Font f = new Font("Arial", 8f, FontStyle.Bold))
                        g.DrawString("C", f, Brushes.White, 2, 1);
                    bmp.Save(path, ImageFormat.Bmp);
                }
                return path;
            }
            catch
            {
                return ""; // SolidWorks tolerates an empty icon path
            }
        }
    }
}
