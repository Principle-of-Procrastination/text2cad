using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using Text2Cad.Addin.Infrastructure;
using Text2Cad.Addin.UI;

namespace Text2Cad.Addin
{
    [ComVisible(true)]
    [Guid(AddinGuid)]
    [ProgId("Text2Cad.Addin")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class SwAddin : ISwAddin
    {
        public const string AddinGuid = "C901B06D-7A6E-4D13-9F9D-2E9C5AE8D0E2";
        private const string ProductTitle = "Text2CAD";
        private const string ProductDescription = "Text2CAD native SOLIDWORKS modeling assistant";

        private ISldWorks? _solidWorks;
        private TaskpaneView? _taskPaneView;
        private TaskPaneControl? _taskPaneControl;

        public bool ConnectToSW(object thisSolidWorks, int cookie)
        {
            try
            {
                AddinLog.Info($"ConnectToSW invoked with cookie {cookie}.");
                _solidWorks = thisSolidWorks as ISldWorks
                    ?? throw new InvalidCastException("SOLIDWORKS did not provide ISldWorks.");

                if (!_solidWorks.SetAddinCallbackInfo2(0, this, cookie))
                {
                    throw new InvalidOperationException("SetAddinCallbackInfo2 returned false.");
                }
                AddinLog.Info("SOLIDWORKS callback information registered.");

                string[] iconPaths = TaskPaneIconSet.EnsureCreated();
                _taskPaneView = _solidWorks.CreateTaskpaneView3(iconPaths, ProductTitle)
                    ?? throw new InvalidOperationException("CreateTaskpaneView3 returned null.");
                AddinLog.Info("SOLIDWORKS Task Pane view created.");

                _taskPaneControl = new TaskPaneControl(_solidWorks);
                IntPtr controlHandle = _taskPaneControl.Handle;
                AddinLog.Info($"Task Pane control handle created: {controlHandle.ToInt64()}.");
                if (!_taskPaneView.DisplayWindowFromHandlex64(controlHandle.ToInt64()))
                {
                    throw new InvalidOperationException("DisplayWindowFromHandlex64 returned false.");
                }

                _taskPaneControl.FitToHost();

                _taskPaneView.ShowView();
                AddinLog.Info("Text2CAD Add-in connected and Task Pane created.");
                return true;
            }
            catch (Exception exception)
            {
                AddinLog.Error("Text2CAD Add-in failed to connect.", exception);
                ReleaseTaskPane();
                _solidWorks = null;
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                ReleaseTaskPane();
                _solidWorks = null;
                AddinLog.Info("Text2CAD Add-in disconnected.");
                return true;
            }
            catch (Exception exception)
            {
                AddinLog.Error("Text2CAD Add-in cleanup failed.", exception);
                _solidWorks = null;
                return false;
            }
        }

        [ComRegisterFunction]
        public static void Register(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            string guid = type.GUID.ToString("B").ToUpperInvariant();
            string addinPath = $"SOFTWARE\\SolidWorks\\Addins\\{guid}";
            string startupPath = $"Software\\SolidWorks\\AddInsStartup\\{guid}";

            using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey addinKey = localMachine.CreateSubKey(addinPath, true)
                ?? throw new InvalidOperationException($"Cannot create HKLM\\{addinPath}.");
            addinKey.SetValue(null, 0, RegistryValueKind.DWord);
            addinKey.SetValue("Title", ProductTitle, RegistryValueKind.String);
            addinKey.SetValue("Description", ProductDescription, RegistryValueKind.String);

            using RegistryKey currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using RegistryKey startupKey = currentUser.CreateSubKey(startupPath, true)
                ?? throw new InvalidOperationException($"Cannot create HKCU\\{startupPath}.");
            startupKey.SetValue(null, 1, RegistryValueKind.DWord);

            AddinLog.Info($"Registered Text2CAD Add-in {guid}.");
        }

        [ComUnregisterFunction]
        public static void Unregister(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            string guid = type.GUID.ToString("B").ToUpperInvariant();
            string addinPath = $"SOFTWARE\\SolidWorks\\Addins\\{guid}";
            string startupPath = $"Software\\SolidWorks\\AddInsStartup\\{guid}";

            using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            localMachine.DeleteSubKeyTree(addinPath, false);

            using RegistryKey currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            currentUser.DeleteSubKeyTree(startupPath, false);

            AddinLog.Info($"Unregistered Text2CAD Add-in {guid}.");
        }

        private void ReleaseTaskPane()
        {
            TaskpaneView? taskPaneView = _taskPaneView;
            _taskPaneView = null;

            if (taskPaneView != null)
            {
                try
                {
                    taskPaneView.DeleteView();
                }
                finally
                {
                    if (Marshal.IsComObject(taskPaneView))
                    {
                        Marshal.ReleaseComObject(taskPaneView);
                    }
                }
            }

            _taskPaneControl?.Dispose();
            _taskPaneControl = null;
        }
    }
}
