using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Text2Cad.Addin.Commands;
using Text2Cad.Addin.Infrastructure;

namespace Text2Cad.Addin.UI
{
    [ComVisible(false)]
    internal sealed class TaskPaneControl : UserControl
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly ISldWorks _solidWorks;
        private readonly CreateTestPlateCommand _createTestPlateCommand;
        private readonly Timer _refreshTimer;
        private readonly Label _connectionValue;
        private readonly Label _documentValue;
        private readonly Label _selectionValue;
        private readonly Label _detailValue;
        private readonly Label _operationValue;
        private readonly Button _createPlateButton;
        private bool _isCreatingPlate;

        public TaskPaneControl(ISldWorks solidWorks)
        {
            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
            _createTestPlateCommand = new CreateTestPlateCommand(_solidWorks);

            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(247, 249, 252);
            ForeColor = Color.FromArgb(24, 34, 53);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(260, 360);
            Size = new Size(360, 720);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = BackColor,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 9
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(CreateHeader(), 0, 0);

            var connectionCard = CreateCard("连接状态", out _connectionValue);
            layout.Controls.Add(connectionCard, 0, 1);

            var documentCard = CreateCard("当前文档", out _documentValue);
            layout.Controls.Add(documentCard, 0, 2);

            var selectionCard = CreateCard("当前选择", out _selectionValue);
            layout.Controls.Add(selectionCard, 0, 3);

            _detailValue = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(4, 12, 4, 0),
                ForeColor = Color.FromArgb(88, 99, 118),
                Text = "API 打通测试：点击下方按钮会新建独立零件，不会修改当前模型。"
            };
            layout.Controls.Add(_detailValue, 0, 4);

            _createPlateButton = new Button
            {
                Text = "新建 100 × 60 × 10 mm 测试板",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 42,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(0, 12, 0, 0)
            };
            _createPlateButton.Click += CreatePlateButton_Click;
            layout.Controls.Add(_createPlateButton, 0, 5);

            _operationValue = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(4, 9, 4, 0),
                ForeColor = Color.FromArgb(88, 99, 118),
                Text = "等待执行"
            };
            layout.Controls.Add(_operationValue, 0, 6);

            var refreshButton = new Button
            {
                Text = "刷新状态",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(4, 12, 4, 0)
            };
            refreshButton.Click += (_, __) => RefreshSnapshot();
            layout.Controls.Add(refreshButton, 0, 8);

            Controls.Add(layout);

            _refreshTimer = new Timer { Interval = 750 };
            _refreshTimer.Tick += (_, __) =>
            {
                FitToHost();
                RefreshSnapshot();
            };
            _refreshTimer.Start();

            RefreshSnapshot();
        }

        internal void FitToHost()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            IntPtr parentHandle = GetParent(Handle);
            if (parentHandle == IntPtr.Zero || !GetClientRect(parentHandle, out NativeRect bounds))
            {
                return;
            }

            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            if (width <= 0 || height <= 0 || (Width == width && Height == height))
            {
                return;
            }

            MoveWindow(Handle, 0, 0, width, height, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Control CreateHeader()
        {
            var panel = new Panel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 14)
            };

            var title = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(24, 34, 53),
                Location = new Point(0, 0),
                Text = "Text2CAD"
            };

            var subtitle = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(88, 99, 118),
                Location = new Point(2, 38),
                Text = "SOLIDWORKS 原生建模助手"
            };

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            panel.Height = 62;
            return panel;
        }

        private static Control CreateCard(string caption, out Label valueLabel)
        {
            var panel = new Panel
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Top,
                Height = 72,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(12, 9, 12, 9)
            };

            var captionLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(104, 116, 137),
                Location = new Point(12, 9),
                Text = caption
            };

            valueLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Bottom,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(24, 34, 53),
                Height = 24,
                Text = "读取中…"
            };

            panel.Controls.Add(captionLabel);
            panel.Controls.Add(valueLabel);
            return panel;
        }

        private void RefreshSnapshot()
        {
            IModelDoc2? document = null;
            ISelectionMgr? selectionManager = null;

            try
            {
                _connectionValue.Text = "● 已连接到 SOLIDWORKS 2025";
                _connectionValue.ForeColor = Color.FromArgb(26, 132, 87);

                document = _solidWorks.IActiveDoc2;
                if (document == null)
                {
                    _documentValue.Text = "未打开文档";
                    _selectionValue.Text = "0 个对象";
                    return;
                }

                string title = document.GetTitle();
                var documentType = (swDocumentTypes_e)document.GetType();
                _documentValue.Text = $"{GetDocumentTypeLabel(documentType)} · {title}";

                selectionManager = document.SelectionManager as ISelectionMgr;
                int selectedCount = selectionManager?.GetSelectedObjectCount2(-1) ?? 0;
                _selectionValue.Text = $"{selectedCount} 个对象";
            }
            catch (COMException exception)
            {
                _connectionValue.Text = "读取状态失败";
                _connectionValue.ForeColor = Color.FromArgb(190, 54, 54);
                _detailValue.Text = "SOLIDWORKS COM 暂时不可用，稍后会自动重试。";
                AddinLog.Error("Failed to refresh Task Pane snapshot.", exception);
            }
            finally
            {
                ReleaseComObject(selectionManager);
                ReleaseComObject(document);
            }
        }

        private void CreatePlateButton_Click(object? sender, EventArgs e)
        {
            if (_isCreatingPlate)
            {
                return;
            }

            _isCreatingPlate = true;
            _createPlateButton.Enabled = false;
            _refreshTimer.Stop();
            _operationValue.ForeColor = Color.FromArgb(50, 91, 160);
            _operationValue.Text = "正在通过 SOLIDWORKS API 新建测试板…";

            try
            {
                string documentTitle = _createTestPlateCommand.Execute();
                _operationValue.ForeColor = Color.FromArgb(26, 132, 87);
                _operationValue.Text = $"✓ 已在 {documentTitle} 中生成原生草图和拉伸凸台。";
            }
            catch (Exception exception)
            {
                _operationValue.ForeColor = Color.FromArgb(190, 54, 54);
                _operationValue.Text = $"创建失败：{exception.Message}";
                AddinLog.Error("Task Pane test plate command failed.", exception);
            }
            finally
            {
                _createPlateButton.Enabled = true;
                _isCreatingPlate = false;
                _refreshTimer.Start();
                RefreshSnapshot();
            }
        }

        private static string GetDocumentTypeLabel(swDocumentTypes_e documentType)
        {
            switch (documentType)
            {
                case swDocumentTypes_e.swDocPART:
                    return "零件";
                case swDocumentTypes_e.swDocASSEMBLY:
                    return "装配体";
                case swDocumentTypes_e.swDocDRAWING:
                    return "工程图";
                default:
                    return "文档";
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect bounds);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveWindow(
            IntPtr windowHandle,
            int x,
            int y,
            int width,
            int height,
            [MarshalAs(UnmanagedType.Bool)] bool repaint);
    }
}
