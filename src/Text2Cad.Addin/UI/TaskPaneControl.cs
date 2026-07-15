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
        private enum TranscriptTone
        {
            Assistant,
            User,
            Success,
            Error,
            Notice
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly ISldWorks _solidWorks;
        private readonly CreatePrimitiveCommand _createPrimitiveCommand;
        private readonly Timer _refreshTimer;
        private readonly Label _connectionValue;
        private readonly Label _contextValue;
        private readonly RichTextBox _conversation;
        private readonly TextBox _promptInput;
        private readonly Button _sendButton;
        private readonly Button[] _exampleButtons;
        private readonly Label _activityValue;
        private readonly Font _chatRoleFont;
        private readonly Font _chatBodyFont;
        private bool _isExecuting;

        public TaskPaneControl(ISldWorks solidWorks)
        {
            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
            _createPrimitiveCommand = new CreatePrimitiveCommand(_solidWorks);

            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(247, 249, 252);
            ForeColor = Color.FromArgb(24, 34, 53);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(260, 420);
            Size = new Size(360, 720);

            _chatBodyFont = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            _chatRoleFont = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = CreateHeader(out _connectionValue, out _contextValue);
            root.Controls.Add(header, 0, 0);

            _conversation = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                DetectUrls = false,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ShortcutsEnabled = true,
                TabStop = false,
                Font = _chatBodyFont,
                Margin = new Padding(0, 4, 0, 10)
            };
            root.Controls.Add(_conversation, 0, 1);

            var composer = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = BackColor,
                ColumnCount = 1,
                RowCount = 5,
                Margin = Padding.Empty
            };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            composer.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(88, 99, 118),
                Text = "试试固定能力",
                Margin = new Padding(2, 0, 2, 4)
            }, 0, 0);

            var examples = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 7),
                Padding = Padding.Empty
            };

            _exampleButtons = new[]
            {
                CreateExampleButton("测试板", "创建一个 100 × 60 × 10 mm 测试板"),
                CreateExampleButton("40 mm 方块", "创建一个边长 40 mm 的方块"),
                CreateExampleButton("Ø40 圆柱", "创建一个直径 40 mm、高 60 mm 的圆柱")
            };
            examples.Controls.AddRange(_exampleButtons);
            composer.Controls.Add(examples, 0, 1);

            var inputRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 62,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            inputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _promptInput = new TextBox
            {
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = _chatBodyFont,
                Margin = new Padding(0, 0, 8, 0)
            };
            _promptInput.KeyDown += PromptInput_KeyDown;
            inputRow.Controls.Add(_promptInput, 0, 0);

            _sendButton = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.System,
                Text = "发送",
                Margin = Padding.Empty
            };
            _sendButton.Click += (_, __) => SubmitPrompt();
            inputRow.Controls.Add(_sendButton, 1, 0);
            composer.Controls.Add(inputRow, 0, 2);

            composer.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(112, 122, 140),
                Text = "Enter 发送 · Shift+Enter 换行",
                Margin = new Padding(2, 5, 2, 0)
            }, 0, 3);

            _activityValue = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(88, 99, 118),
                Text = "等待输入",
                Margin = new Padding(2, 5, 2, 0)
            };
            composer.Controls.Add(_activityValue, 0, 4);
            root.Controls.Add(composer, 0, 2);

            Controls.Add(root);

            _refreshTimer = new Timer { Interval = 750 };
            _refreshTimer.Tick += (_, __) =>
            {
                FitToHost();
                RefreshSnapshot();
            };
            _refreshTimer.Start();

            AppendMessage(
                "Text2CAD",
                "你好，我已经连接到 SOLIDWORKS。输入一句话，我会用固定规则调用真实 CAD API。\n" +
                ChatCommandRouter.SupportedCapabilities,
                TranscriptTone.Assistant);
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
                _chatRoleFont.Dispose();
                _chatBodyFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Control CreateHeader(out Label connectionValue, out Label contextValue)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 86,
                Margin = new Padding(0, 0, 0, 6)
            };

            var title = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(24, 34, 53),
                Location = new Point(0, 0),
                Text = "Text2CAD"
            };

            var badge = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(225, 235, 252),
                ForeColor = Color.FromArgb(50, 91, 160),
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(112, 9),
                Padding = new Padding(5, 2, 5, 2),
                Text = "DEMO"
            };

            connectionValue = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(26, 132, 87),
                Location = new Point(2, 43),
                Text = "● 正在连接 SOLIDWORKS…"
            };

            var contextLabel = new Label
            {
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(88, 99, 118),
                Location = new Point(2, 64),
                Height = 19,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Text = "读取当前文档…"
            };
            contextValue = contextLabel;

            panel.Resize += (_, __) => contextLabel.Width = Math.Max(40, panel.ClientSize.Width - 4);
            panel.Controls.Add(title);
            panel.Controls.Add(badge);
            panel.Controls.Add(connectionValue);
            panel.Controls.Add(contextLabel);
            return panel;
        }

        private Button CreateExampleButton(string label, string prompt)
        {
            var button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.System,
                Height = 28,
                Text = label,
                Margin = new Padding(0, 0, 6, 3),
                Padding = new Padding(3, 0, 3, 0)
            };
            button.Click += (_, __) =>
            {
                _promptInput.Text = prompt;
                SubmitPrompt();
            };
            return button;
        }

        private void PromptInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || e.Shift)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            SubmitPrompt();
        }

        private void SubmitPrompt()
        {
            if (_isExecuting)
            {
                return;
            }

            string prompt = _promptInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _activityValue.ForeColor = Color.FromArgb(183, 115, 24);
                _activityValue.Text = "请先输入一条指令";
                _promptInput.Focus();
                return;
            }

            _promptInput.Clear();
            AppendMessage("你", prompt, TranscriptTone.User);

            ChatRouteResult route = ChatCommandRouter.Route(prompt);
            if (!route.ShouldExecute)
            {
                AppendMessage("Text2CAD", route.AssistantMessage, TranscriptTone.Notice);
                _activityValue.ForeColor = Color.FromArgb(88, 99, 118);
                _activityValue.Text = "等待输入";
                _promptInput.Focus();
                return;
            }

            AppendMessage("Text2CAD", route.AssistantMessage, TranscriptTone.Assistant);
            _isExecuting = true;
            SetComposerEnabled(false);
            _refreshTimer.Stop();
            _activityValue.ForeColor = Color.FromArgb(50, 91, 160);
            _activityValue.Text = route.BusyMessage;
            Refresh();

            PrimitiveKind primitive = route.Primitive!.Value;
            try
            {
                BeginInvoke(new Action(() => ExecutePrimitive(primitive)));
            }
            catch (InvalidOperationException exception)
            {
                AddinLog.Error("Could not queue the Task Pane chat command.", exception);
                if (CanUpdateUi)
                {
                    AppendMessage("Text2CAD", "执行失败：面板正在关闭，请重新打开后再试。", TranscriptTone.Error);
                    _isExecuting = false;
                    SetComposerEnabled(true);
                    _refreshTimer.Start();
                }
            }
        }

        private void ExecutePrimitive(PrimitiveKind primitive)
        {
            if (!CanUpdateUi)
            {
                return;
            }

            try
            {
                PrimitiveCreationResult result = _createPrimitiveCommand.Execute(primitive);
                if (CanUpdateUi)
                {
                    AppendMessage(
                        "Text2CAD",
                        $"✓ 已在 {result.DocumentTitle} 中创建 {result.ShapeDescription}，结果是可继续编辑的原生草图和拉伸特征。",
                        TranscriptTone.Success);
                    _activityValue.ForeColor = Color.FromArgb(26, 132, 87);
                    _activityValue.Text = "执行成功";
                }
            }
            catch (Exception exception)
            {
                if (CanUpdateUi)
                {
                    AppendMessage("Text2CAD", $"执行失败：{exception.Message}", TranscriptTone.Error);
                    _activityValue.ForeColor = Color.FromArgb(190, 54, 54);
                    _activityValue.Text = "执行失败";
                }
                AddinLog.Error("Task Pane chat command failed.", exception);
            }
            finally
            {
                if (CanUpdateUi)
                {
                    _isExecuting = false;
                    SetComposerEnabled(true);
                    _refreshTimer.Start();
                    RefreshSnapshot();
                    _promptInput.Focus();
                }
            }
        }

        private bool CanUpdateUi => !IsDisposed && !Disposing && IsHandleCreated;

        private void SetComposerEnabled(bool enabled)
        {
            _promptInput.Enabled = enabled;
            _sendButton.Enabled = enabled;
            foreach (Button button in _exampleButtons)
            {
                button.Enabled = enabled;
            }
        }

        private void AppendMessage(string role, string message, TranscriptTone tone)
        {
            Color roleColor;
            Color bodyColor;

            switch (tone)
            {
                case TranscriptTone.User:
                    roleColor = Color.FromArgb(50, 91, 160);
                    bodyColor = Color.FromArgb(35, 68, 122);
                    break;
                case TranscriptTone.Success:
                    roleColor = Color.FromArgb(26, 132, 87);
                    bodyColor = Color.FromArgb(26, 105, 73);
                    break;
                case TranscriptTone.Error:
                    roleColor = Color.FromArgb(190, 54, 54);
                    bodyColor = Color.FromArgb(154, 42, 42);
                    break;
                case TranscriptTone.Notice:
                    roleColor = Color.FromArgb(183, 115, 24);
                    bodyColor = Color.FromArgb(128, 83, 24);
                    break;
                default:
                    roleColor = Color.FromArgb(24, 34, 53);
                    bodyColor = Color.FromArgb(50, 60, 78);
                    break;
            }

            _conversation.SelectionStart = _conversation.TextLength;
            _conversation.SelectionLength = 0;
            _conversation.SelectionFont = _chatRoleFont;
            _conversation.SelectionColor = roleColor;
            _conversation.AppendText(role + System.Environment.NewLine);

            _conversation.SelectionFont = _chatBodyFont;
            _conversation.SelectionColor = bodyColor;
            _conversation.AppendText(
                message.Replace("\n", System.Environment.NewLine) +
                System.Environment.NewLine +
                System.Environment.NewLine);

            _conversation.SelectionStart = _conversation.TextLength;
            _conversation.SelectionLength = 0;
            _conversation.ScrollToCaret();
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
                    _contextValue.Text = "未打开文档 · 已选 0 个对象";
                    return;
                }

                string title = document.GetTitle();
                var documentType = (swDocumentTypes_e)document.GetType();
                selectionManager = document.SelectionManager as ISelectionMgr;
                int selectedCount = selectionManager?.GetSelectedObjectCount2(-1) ?? 0;
                _contextValue.Text = $"{GetDocumentTypeLabel(documentType)} · {title} · 已选 {selectedCount} 个对象";
            }
            catch (COMException exception)
            {
                _connectionValue.Text = "● SOLIDWORKS 状态读取失败";
                _connectionValue.ForeColor = Color.FromArgb(190, 54, 54);
                _contextValue.Text = "稍后会自动重试";
                AddinLog.Error("Failed to refresh Task Pane snapshot.", exception);
            }
            finally
            {
                ReleaseComObject(selectionManager);
                ReleaseComObject(document);
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
