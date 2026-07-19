using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private const string PromptPlaceholder = "描述要创建或修改的模型";

        private static readonly Color CanvasColor = Color.FromArgb(246, 248, 251);
        private static readonly Color SurfaceColor = Color.FromArgb(255, 255, 255);
        private static readonly Color SurfaceMutedColor = Color.FromArgb(249, 250, 252);
        private static readonly Color BorderColor = Color.FromArgb(220, 225, 233);
        private static readonly Color BorderStrongColor = Color.FromArgb(198, 207, 220);
        private static readonly Color TextPrimaryColor = Color.FromArgb(28, 35, 48);
        private static readonly Color TextMutedColor = Color.FromArgb(119, 128, 144);
        private static readonly Color AccentColor = Color.FromArgb(51, 94, 166);
        private static readonly Color AccentHoverColor = Color.FromArgb(43, 80, 143);
        private static readonly Color AccentSoftColor = Color.FromArgb(234, 240, 250);
        private static readonly Color AccentDisabledColor = Color.FromArgb(194, 205, 223);
        private static readonly Color UserMessageColor = Color.FromArgb(246, 249, 253);
        private static readonly Color UserMessageBorderColor = Color.FromArgb(181, 198, 224);
        private static readonly Color SuccessColor = Color.FromArgb(31, 130, 91);
        private static readonly Color SuccessSoftColor = Color.FromArgb(231, 246, 239);
        private static readonly Color WarningColor = Color.FromArgb(174, 105, 20);
        private static readonly Color ErrorColor = Color.FromArgb(184, 54, 62);
        private static readonly Color ErrorSoftColor = Color.FromArgb(252, 235, 237);

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
        private readonly List<Font> _ownedFonts = new List<Font>();
        private readonly ToolTip _toolTip;
        private readonly ContextMenuStrip _messageContextMenu;
        private readonly Timer _refreshTimer;
        private readonly Label _connectionValue;
        private readonly Label _contextValue;
        private readonly FlowLayoutPanel _conversation;
        private readonly PromptTextBox _promptInput;
        private readonly Button _sendButton;
        private readonly Button[] _exampleButtons;
        private readonly Label _activityValue;
        private readonly Font _chatRoleFont;
        private readonly Font _chatBodyFont;
        private readonly List<MessageView> _messageViews = new List<MessageView>();
        private FlowLayoutPanel? _quickActionsPanel;
        private bool? _isCompactLayout;
        private bool _hostLayoutRefreshQueued;
        private bool _isUpdatingMessageLayout;
        private bool _isShuttingDown;
        private bool _resourcesDisposed;
        private bool _refreshFailureLogged;
        private bool _snapshotFailureLogged;
        private bool _isExecuting;
        private bool _isPromptPlaceholderVisible;

        public TaskPaneControl(ISldWorks solidWorks)
        {
            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
            _createPrimitiveCommand = new CreatePrimitiveCommand(_solidWorks);

            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BackColor = CanvasColor;
            ForeColor = TextPrimaryColor;
            Font = OwnFont(9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(260, 420);
            Size = new Size(360, 720);

            _chatBodyFont = OwnFont(9.5F);
            _chatRoleFont = OwnFont(9F, FontStyle.Bold);
            _toolTip = new ToolTip
            {
                AutomaticDelay = 350,
                AutoPopDelay = 6000,
                InitialDelay = 350,
                ReshowDelay = 100,
                ShowAlways = true
            };
            _messageContextMenu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };
            var copyMessageItem = new ToolStripMenuItem("复制消息");
            copyMessageItem.Click += (_, __) =>
            {
                if (!(_messageContextMenu.SourceControl is Label source) ||
                    string.IsNullOrEmpty(source.Text))
                {
                    return;
                }

                try
                {
                    Clipboard.SetText(source.Text);
                }
                catch (ExternalException)
                {
                    if (CanUpdateUi)
                    {
                        SetActivity("复制失败，请重试", WarningColor);
                    }
                }
            };
            _messageContextMenu.Items.Add(copyMessageItem);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 4
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control header = CreateHeader(out _connectionValue);
            root.Controls.Add(header, 0, 0);

            Control context = CreateContextCard(out _contextValue);
            root.Controls.Add(context, 0, 1);

            var conversationSurface = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(9, 8, 7, 8)
            };

            _conversation = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = SurfaceColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                TabStop = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            _conversation.ClientSizeChanged += (_, __) => UpdateMessageLayout();
            conversationSurface.Controls.Add(_conversation);
            root.Controls.Add(conversationSurface, 0, 2);

            var composerCard = new SurfacePanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = SurfaceColor,
                BorderColor = BorderColor,
                CornerRadius = 9,
                Margin = Padding.Empty,
                Padding = new Padding(8)
            };
            var composer = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = SurfaceColor,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty
            };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            composer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _quickActionsPanel = new FlowLayoutPanel
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = SurfaceColor,
                Margin = new Padding(0, 0, 0, 4),
                Padding = Padding.Empty
            };
            _quickActionsPanel.Layout += (_, __) => FitQuickActionsHeight();

            _exampleButtons = new[]
            {
                CreateExampleButton("测试板", "创建一个 100 × 60 × 10 mm 测试板"),
                CreateExampleButton("40 mm 方块", "创建一个边长 40 mm 的方块"),
                CreateExampleButton("Ø40 圆柱", "创建一个直径 40 mm、高 60 mm 的圆柱")
            };
            _quickActionsPanel.Controls.AddRange(_exampleButtons);
            composer.Controls.Add(_quickActionsPanel, 0, 0);

            var inputFrame = new SurfacePanel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = SurfaceColor,
                BorderColor = BorderStrongColor,
                CornerRadius = 7,
                Margin = Padding.Empty,
                Padding = new Padding(9, 8, 7, 6)
            };

            _promptInput = new PromptTextBox
            {
                AcceptsReturn = true,
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.None,
                BackColor = SurfaceColor,
                BorderStyle = BorderStyle.None,
                ForeColor = TextMutedColor,
                Font = _chatBodyFont,
                Margin = Padding.Empty,
                Text = PromptPlaceholder,
                AccessibleName = "建模指令"
            };
            _isPromptPlaceholderVisible = true;
            _promptInput.SendRequested += (_, __) => SubmitPrompt();
            _promptInput.Enter += (_, __) =>
            {
                inputFrame.BorderColor = AccentColor;
                HidePromptPlaceholder();
            };
            _promptInput.Leave += (_, __) =>
            {
                inputFrame.BorderColor = BorderStrongColor;
                if (string.IsNullOrWhiteSpace(_promptInput.Text))
                {
                    ShowPromptPlaceholder();
                }
            };
            inputFrame.Controls.Add(_promptInput);
            _toolTip.SetToolTip(_promptInput, "Enter 发送，Shift+Enter 换行。");
            composer.Controls.Add(inputFrame, 0, 1);

            var actionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = SurfaceColor,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 6, 0, 0),
                Padding = Padding.Empty
            };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68F));
            actionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var modeBadge = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                BackColor = AccentSoftColor,
                ForeColor = AccentColor,
                Font = OwnFont(8F, FontStyle.Bold),
                Padding = new Padding(7, 4, 7, 4),
                Text = "固定规则",
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 4, 0, 0)
            };
            _toolTip.SetToolTip(modeBadge, "识别三类固定指令并直接调用 SOLIDWORKS 2026 API。");
            actionRow.Controls.Add(modeBadge, 0, 0);

            _activityValue = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = TextMutedColor,
                Font = OwnFont(8F),
                Text = "Enter 发送",
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(6, 0, 7, 0)
            };
            actionRow.Controls.Add(_activityValue, 1, 0);

            _sendButton = new Button
            {
                Dock = DockStyle.Fill,
                BackColor = AccentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = OwnFont(9F, FontStyle.Bold),
                Text = "发送",
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Margin = Padding.Empty
            };
            _sendButton.FlatAppearance.BorderSize = 0;
            _sendButton.FlatAppearance.MouseOverBackColor = AccentHoverColor;
            _sendButton.FlatAppearance.MouseDownBackColor = AccentHoverColor;
            _sendButton.Click += (_, __) => SubmitPrompt();
            _toolTip.SetToolTip(_sendButton, "发送建模指令");
            actionRow.Controls.Add(_sendButton, 2, 0);
            composer.Controls.Add(actionRow, 0, 2);

            _promptInput.TextChanged += (_, __) => UpdateSendButtonState();
            UpdateSendButtonState();

            composerCard.Controls.Add(composer);
            root.Controls.Add(composerCard, 0, 3);

            Controls.Add(root);
            UpdateResponsiveLayout();

            _refreshTimer = new Timer { Interval = 1250 };
            _refreshTimer.Tick += RefreshTimer_Tick;

            AppendMessage(
                "Text2CAD",
                "已连接到 SOLIDWORKS 2026。可以直接创建测试板、方块或圆柱。",
                TranscriptTone.Assistant);
        }

        internal void StartMonitoring()
        {
            if (_isShuttingDown || _refreshTimer.Enabled)
            {
                return;
            }

            RefreshHostedLayout();
            QueueHostedLayoutRefresh();
            RefreshSnapshot();
            _refreshTimer.Start();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RefreshHostedLayout();
            QueueHostedLayoutRefresh();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _isCompactLayout = null;
            RefreshHostedLayout();
            QueueHostedLayoutRefresh();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            _isCompactLayout = null;
            RefreshHostedLayout();
            QueueHostedLayoutRefresh();
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
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (Width != width || Height != height)
            {
                MoveWindow(Handle, 0, 0, width, height, true);
            }

            FitQuickActionsHeight();
        }

        internal void BeginShutdown()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            _refreshTimer.Tick -= RefreshTimer_Tick;
            _refreshTimer.Stop();
        }

        private void UpdateResponsiveLayout()
        {
            if (_quickActionsPanel == null || _exampleButtons == null)
            {
                return;
            }

            bool isCompact = Width < ScaleLogicalPixels(300);
            if (_isCompactLayout != isCompact)
            {
                _isCompactLayout = isCompact;
                string[] labels = isCompact
                    ? new[] { "测试板", "方块", "圆柱" }
                    : new[] { "测试板", "40 mm 方块", "Ø40 圆柱" };
                Padding buttonMargin = isCompact
                    ? new Padding(0, 0, 4, 3)
                    : new Padding(0, 0, 6, 3);
                Padding buttonPadding = isCompact
                    ? new Padding(5, 2, 5, 2)
                    : new Padding(7, 2, 7, 2);

                for (int index = 0; index < _exampleButtons.Length; index++)
                {
                    _exampleButtons[index].Text = labels[index];
                    _exampleButtons[index].Margin = buttonMargin;
                    _exampleButtons[index].Padding = buttonPadding;
                }
            }

            _quickActionsPanel.Visible = true;
            _quickActionsPanel.PerformLayout();
            FitQuickActionsHeight();
            PerformLayout();
        }

        private void FitQuickActionsHeight()
        {
            if (_quickActionsPanel == null)
            {
                return;
            }

            int requiredHeight = 0;
            foreach (Control child in _quickActionsPanel.Controls)
            {
                if (child.Visible)
                {
                    requiredHeight = Math.Max(requiredHeight, child.Bottom);
                }
            }

            if (requiredHeight > 0 && _quickActionsPanel.Height != requiredHeight)
            {
                _quickActionsPanel.Height = requiredHeight;
            }
        }

        private void RefreshHostedLayout()
        {
            if (_isShuttingDown)
            {
                return;
            }

            UpdateResponsiveLayout();
            UpdateMessageLayout();
        }

        private void QueueHostedLayoutRefresh()
        {
            if (
                _hostLayoutRefreshQueued ||
                _isShuttingDown ||
                !IsHandleCreated)
            {
                return;
            }

            _hostLayoutRefreshQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _hostLayoutRefreshQueued = false;
                    if (CanUpdateUi)
                    {
                        RefreshHostedLayout();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                _hostLayoutRefreshQueued = false;
            }
        }

        private int ScaleLogicalPixels(int logicalPixels)
        {
            return (int)Math.Round(logicalPixels * Math.Max(96, DeviceDpi) / 96F);
        }

        protected override void Dispose(bool disposing)
        {
            bool disposeResources = disposing && !_resourcesDisposed;
            if (disposeResources)
            {
                _resourcesDisposed = true;
                try
                {
                    BeginShutdown();
                }
                catch (Exception exception)
                {
                    AddinLog.Error("Task Pane refresh loop could not be stopped during disposal.", exception);
                }

                try
                {
                    _refreshTimer.Dispose();
                }
                catch (Exception exception)
                {
                    AddinLog.Error("Task Pane refresh timer could not be disposed.", exception);
                }

                try
                {
                    _toolTip.Dispose();
                }
                catch (Exception exception)
                {
                    AddinLog.Error("Task Pane tooltips could not be disposed.", exception);
                }

                try
                {
                    _messageContextMenu.Dispose();
                }
                catch (Exception exception)
                {
                    AddinLog.Error("Task Pane message menu could not be disposed.", exception);
                }
            }

            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                if (disposeResources)
                {
                    foreach (Font font in _ownedFonts)
                    {
                        try
                        {
                            font.Dispose();
                        }
                        catch (Exception exception)
                        {
                            AddinLog.Error("A Task Pane font could not be disposed.", exception);
                        }
                    }
                    _ownedFonts.Clear();
                }
            }
        }

        private Control CreateHeader(out Label connectionValue)
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = CanvasColor,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(1, 1, 0, 1)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            header.Controls.Add(new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = OwnFont(12.5F, FontStyle.Bold),
                ForeColor = TextPrimaryColor,
                Text = "Text2CAD",
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 1, 0, 0)
            }, 0, 0);

            connectionValue = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = SuccessSoftColor,
                ForeColor = SuccessColor,
                Font = OwnFont(8F, FontStyle.Bold),
                Padding = new Padding(7, 4, 7, 4),
                Text = "● SW2026",
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(8, 1, 0, 0)
            };
            _toolTip.SetToolTip(connectionValue, "Text2CAD 已连接到本机 SOLIDWORKS 2026。");
            header.Controls.Add(connectionValue, 1, 0);
            return header;
        }

        private Control CreateContextCard(out Label contextValue)
        {
            var card = new SurfacePanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = SurfaceMutedColor,
                BorderColor = BorderColor,
                CornerRadius = 7,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 6, 8, 5)
            };

            var contextLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                AutoSize = false,
                BackColor = SurfaceMutedColor,
                Font = OwnFont(8.5F),
                ForeColor = TextPrimaryColor,
                Text = "当前 · 正在读取文档与选择…",
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            Action fitContextCardHeight = () =>
            {
                int requiredHeight = Math.Max(
                    ScaleLogicalPixels(34),
                    contextLabel.Font.Height + card.Padding.Vertical + ScaleLogicalPixels(2));
                if (card.Height != requiredHeight)
                {
                    card.Height = requiredHeight;
                }
            };

            card.Layout += (_, __) => fitContextCardHeight();
            contextLabel.FontChanged += (_, __) => fitContextCardHeight();
            card.Controls.Add(contextLabel);
            fitContextCardHeight();

            contextValue = contextLabel;
            return card;
        }

        private Button CreateExampleButton(string label, string prompt)
        {
            var button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SurfaceMutedColor,
                ForeColor = TextPrimaryColor,
                FlatStyle = FlatStyle.Flat,
                Font = OwnFont(8.5F),
                Height = 29,
                Text = label,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Margin = new Padding(0, 0, 6, 3),
                Padding = new Padding(7, 2, 7, 2)
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = AccentSoftColor;
            button.FlatAppearance.MouseDownBackColor = AccentSoftColor;
            _toolTip.SetToolTip(button, prompt);
            button.Click += (_, __) =>
            {
                SetPromptText(prompt);
                SubmitPrompt();
            };
            return button;
        }

        private void SetPromptText(string text)
        {
            _isPromptPlaceholderVisible = false;
            _promptInput.ForeColor = TextPrimaryColor;
            _promptInput.Text = text;
            _promptInput.SelectionStart = _promptInput.TextLength;
        }

        private void HidePromptPlaceholder()
        {
            if (!_isPromptPlaceholderVisible)
            {
                return;
            }

            _isPromptPlaceholderVisible = false;
            _promptInput.Clear();
            _promptInput.ForeColor = TextPrimaryColor;
        }

        private void ShowPromptPlaceholder()
        {
            _isPromptPlaceholderVisible = true;
            _promptInput.ForeColor = TextMutedColor;
            _promptInput.Text = PromptPlaceholder;
            _promptInput.SelectionStart = 0;
            _promptInput.SelectionLength = 0;
        }

        private Font OwnFont(float size, FontStyle style = FontStyle.Regular)
        {
            var font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            _ownedFonts.Add(font);
            return font;
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (_isShuttingDown || _isExecuting || !CanUpdateUi)
            {
                return;
            }

            try
            {
                FitToHost();
                RefreshSnapshot();
                _refreshFailureLogged = false;
            }
            catch (Exception exception)
            {
                if (!_refreshFailureLogged)
                {
                    AddinLog.Error("Task Pane refresh loop failed; it will retry.", exception);
                    _refreshFailureLogged = true;
                }
            }
        }

        private void SubmitPrompt()
        {
            if (_isExecuting)
            {
                return;
            }

            string prompt = _isPromptPlaceholderVisible
                ? string.Empty
                : _promptInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                SetActivity("请先输入一条指令", WarningColor);
                _promptInput.Focus();
                return;
            }

            _promptInput.Clear();
            AppendMessage("你", prompt, TranscriptTone.User);

            ChatRouteResult route = ChatCommandRouter.Route(prompt);
            if (!route.ShouldExecute)
            {
                AppendMessage("Text2CAD", route.AssistantMessage, TranscriptTone.Notice);
                SetActivity("就绪", SuccessColor);
                _promptInput.Focus();
                return;
            }

            AppendMessage("Text2CAD", route.AssistantMessage, TranscriptTone.Assistant);
            _isExecuting = true;
            SetComposerEnabled(false);
            _refreshTimer.Stop();
            SetActivity(route.BusyMessage, AccentColor);
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
                    SetActivity("面板正在关闭", ErrorColor);
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
                    SetActivity("执行成功", SuccessColor);
                }
            }
            catch (Exception exception)
            {
                if (CanUpdateUi)
                {
                    AppendMessage("Text2CAD", $"执行失败：{exception.Message}", TranscriptTone.Error);
                    SetActivity("执行失败", ErrorColor);
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

        private bool CanUpdateUi => !_isShuttingDown && !IsDisposed && !Disposing && IsHandleCreated;

        private void UpdateSendButtonState()
        {
            bool hasPrompt =
                !_isPromptPlaceholderVisible &&
                !string.IsNullOrWhiteSpace(_promptInput.Text);
            bool isEnabled =
                !_isExecuting &&
                _promptInput.Enabled &&
                hasPrompt;
            _sendButton.Enabled = isEnabled;
            _sendButton.BackColor = isEnabled ? AccentColor : AccentDisabledColor;
            _sendButton.Cursor = isEnabled ? Cursors.Hand : Cursors.Default;
        }

        private void SetActivity(string message, Color color)
        {
            if (string.Equals(message, "就绪", StringComparison.Ordinal))
            {
                _activityValue.ForeColor = TextMutedColor;
                _activityValue.Text = "Enter 发送";
                return;
            }

            _activityValue.ForeColor = color;
            _activityValue.Text = "● " + message;
        }

        private void SetComposerEnabled(bool enabled)
        {
            _promptInput.Enabled = enabled;
            _sendButton.Text = enabled ? "发送" : "执行中";
            foreach (Button button in _exampleButtons)
            {
                button.Enabled = enabled;
            }

            if (enabled)
            {
                UpdateSendButtonState();
            }
            else
            {
                _sendButton.Enabled = false;
                _sendButton.BackColor = AccentDisabledColor;
                _sendButton.Cursor = Cursors.Default;
            }
        }

        private void AppendMessage(string role, string message, TranscriptTone tone)
        {
            Color roleColor;
            Color bodyColor;

            switch (tone)
            {
                case TranscriptTone.User:
                    roleColor = AccentColor;
                    bodyColor = Color.FromArgb(43, 72, 121);
                    break;
                case TranscriptTone.Success:
                    roleColor = SuccessColor;
                    bodyColor = Color.FromArgb(31, 108, 80);
                    break;
                case TranscriptTone.Error:
                    roleColor = ErrorColor;
                    bodyColor = Color.FromArgb(151, 47, 54);
                    break;
                case TranscriptTone.Notice:
                    roleColor = WarningColor;
                    bodyColor = Color.FromArgb(130, 82, 22);
                    break;
                default:
                    roleColor = TextPrimaryColor;
                    bodyColor = Color.FromArgb(59, 67, 81);
                    break;
            }

            string normalizedMessage = NormalizeMessageText(message);
            MessageView view = tone == TranscriptTone.User
                ? CreateUserMessageView(normalizedMessage, bodyColor)
                : CreateSystemMessageView(role, normalizedMessage, roleColor, bodyColor);

            _messageViews.Add(view);
            _conversation.Controls.Add(view.Row);
            UpdateMessageLayout();
            _conversation.PerformLayout();
            if (CanUpdateUi)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (CanUpdateUi && !view.Row.IsDisposed)
                        {
                            _conversation.ScrollControlIntoView(view.Row);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // The Task Pane can close between appending and scheduling the scroll.
                }
            }
        }

        private MessageView CreateUserMessageView(string message, Color bodyColor)
        {
            var row = CreateMessageRow(1);
            row.ColumnCount = 2;
            row.ColumnStyles.Clear();
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var bubble = new SurfacePanel
            {
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = UserMessageColor,
                BorderColor = UserMessageBorderColor,
                CornerRadius = 8,
                Margin = Padding.Empty,
                Padding = new Padding(10, 7, 10, 7)
            };

            var body = new Label
            {
                AutoSize = false,
                BackColor = UserMessageColor,
                ForeColor = bodyColor,
                Font = _chatBodyFont,
                Text = message,
                TextAlign = ContentAlignment.TopLeft,
                UseMnemonic = false,
                ContextMenuStrip = _messageContextMenu,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            bubble.Controls.Add(body);
            row.Controls.Add(bubble, 1, 0);

            return new MessageView(row, body, bubble, true);
        }

        private MessageView CreateSystemMessageView(
            string role,
            string message,
            Color roleColor,
            Color bodyColor)
        {
            var row = CreateMessageRow(2);
            row.Controls.Add(new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                ForeColor = roleColor,
                Font = _chatRoleFont,
                Text = role,
                TextAlign = ContentAlignment.TopLeft,
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, 3),
                Padding = Padding.Empty
            }, 0, 0);

            var body = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                ForeColor = bodyColor,
                Font = _chatBodyFont,
                MaximumSize = new Size(ScaleLogicalPixels(320), 0),
                Text = message,
                TextAlign = ContentAlignment.TopLeft,
                UseMnemonic = false,
                ContextMenuStrip = _messageContextMenu,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            row.Controls.Add(body, 0, 1);

            return new MessageView(row, body, null, false);
        }

        private TableLayoutPanel CreateMessageRow(int rowCount)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SurfaceColor,
                ColumnCount = 1,
                RowCount = rowCount,
                Margin = new Padding(0, 0, 0, 12),
                Padding = Padding.Empty
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int index = 0; index < rowCount; index++)
            {
                row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            return row;
        }

        private void UpdateMessageLayout()
        {
            if (_isUpdatingMessageLayout || _conversation == null)
            {
                return;
            }

            _isUpdatingMessageLayout = true;
            _conversation.SuspendLayout();
            try
            {
                int rowWidth = Math.Max(
                    ScaleLogicalPixels(80),
                    _conversation.ClientSize.Width -
                    _conversation.Padding.Horizontal -
                    ScaleLogicalPixels(1));

                foreach (MessageView view in _messageViews)
                {
                    view.Row.MinimumSize = new Size(rowWidth, 0);
                    view.Row.MaximumSize = new Size(rowWidth, 0);
                    view.Row.Width = rowWidth;
                    if (view.IsUser && view.Bubble != null)
                    {
                        int bubbleWidth = Math.Min(
                            rowWidth,
                            Math.Max(
                                ScaleLogicalPixels(120),
                                (int)Math.Round(rowWidth * 0.82F)));
                        int bodyWidth = Math.Max(
                            ScaleLogicalPixels(40),
                            bubbleWidth -
                            view.Bubble.Padding.Horizontal -
                            ScaleLogicalPixels(2));
                        Size measuredBody = TextRenderer.MeasureText(
                            view.Body.Text,
                            view.Body.Font,
                            new Size(bodyWidth, int.MaxValue),
                            TextFormatFlags.NoPadding |
                            TextFormatFlags.NoPrefix |
                            TextFormatFlags.TextBoxControl |
                            TextFormatFlags.WordBreak);
                        measuredBody.Width = Math.Min(
                            bodyWidth,
                            Math.Max(ScaleLogicalPixels(12), measuredBody.Width));
                        measuredBody.Height = Math.Max(
                            view.Body.Font.Height,
                            measuredBody.Height);

                        view.Body.Bounds = new Rectangle(
                            view.Bubble.Padding.Left,
                            view.Bubble.Padding.Top,
                            measuredBody.Width,
                            measuredBody.Height);
                        view.Bubble.Size = new Size(
                            measuredBody.Width + view.Bubble.Padding.Horizontal,
                            measuredBody.Height + view.Bubble.Padding.Vertical);
                    }
                    else
                    {
                        view.Body.MaximumSize = new Size(rowWidth, 0);
                    }

                    view.Row.PerformLayout();
                }

                _conversation.PerformLayout();
            }
            finally
            {
                _conversation.ResumeLayout(true);
                _isUpdatingMessageLayout = false;
            }
        }

        private static string NormalizeMessageText(string message)
        {
            return message
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", System.Environment.NewLine);
        }

        private void RefreshSnapshot()
        {
            IModelDoc2? document = null;
            ISelectionMgr? selectionManager = null;

            try
            {
                _connectionValue.Text = "● SW2026";
                _connectionValue.ForeColor = SuccessColor;
                _connectionValue.BackColor = SuccessSoftColor;

                document = _solidWorks.IActiveDoc2;
                if (document == null)
                {
                    _contextValue.Text = "当前 · 未打开文档 · 已选 0 个对象";
                    _snapshotFailureLogged = false;
                    return;
                }

                string title = document.GetTitle();
                var documentType = (swDocumentTypes_e)document.GetType();
                selectionManager = document.SelectionManager as ISelectionMgr;
                int selectedCount = selectionManager?.GetSelectedObjectCount2(-1) ?? 0;
                _contextValue.Text = $"当前 · {GetDocumentTypeLabel(documentType)} · {title} · 已选 {selectedCount} 个对象";
                _snapshotFailureLogged = false;
            }
            catch (Exception exception)
            {
                _connectionValue.Text = "● 状态异常";
                _connectionValue.ForeColor = ErrorColor;
                _connectionValue.BackColor = ErrorSoftColor;
                _contextValue.Text = "当前 · 状态读取失败，稍后自动重试";
                if (!_snapshotFailureLogged)
                {
                    AddinLog.Error("Failed to refresh Task Pane snapshot; it will retry.", exception);
                    _snapshotFailureLogged = true;
                }
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
            if (value == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
            catch (COMException)
            {
                // SOLIDWORKS may invalidate transient wrappers during shutdown.
            }
            catch (InvalidComObjectException)
            {
                // The wrapper has already been released by the host.
            }
        }

        private sealed class MessageView
        {
            public MessageView(
                TableLayoutPanel row,
                Label body,
                SurfacePanel? bubble,
                bool isUser)
            {
                Row = row;
                Body = body;
                Bubble = bubble;
                IsUser = isUser;
            }

            public TableLayoutPanel Row { get; }

            public Label Body { get; }

            public SurfacePanel? Bubble { get; }

            public bool IsUser { get; }
        }

        private sealed class PromptTextBox : TextBox
        {
            private const int DlgcWantMessage = 0x0004;
            private const int VirtualKeyReturn = 0x000D;
            private const int WmGetDlgCode = 0x0087;
            private const int WmKeyDown = 0x0100;
            private const int WmImeStartComposition = 0x010D;
            private const int WmImeEndComposition = 0x010E;

            private bool _handledEnterIsDown;
            private bool _imeEnterIsDown;
            private bool _isImeComposing;
            private bool _suppressEnterUntilKeyUp;

            public event EventHandler? SendRequested;

            protected override bool IsInputKey(Keys keyData)
            {
                if ((keyData & Keys.KeyCode) == Keys.Enter)
                {
                    return true;
                }

                return base.IsInputKey(keyData);
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                if (TryHandleEnter(keyData))
                {
                    return true;
                }

                return base.ProcessCmdKey(ref message, keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (TryHandleEnter(e.KeyData))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                base.OnKeyDown(e);
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ResetEnterState();
                }

                base.OnKeyUp(e);
            }

            protected override void OnLostFocus(EventArgs e)
            {
                ResetEnterState();
                base.OnLostFocus(e);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmGetDlgCode &&
                    message.WParam.ToInt64() == VirtualKeyReturn)
                {
                    base.WndProc(ref message);
                    message.Result = new IntPtr(message.Result.ToInt64() | DlgcWantMessage);
                    return;
                }

                if (message.Msg == WmImeStartComposition)
                {
                    _isImeComposing = true;
                }
                else if (
                    message.Msg == WmKeyDown &&
                    message.WParam.ToInt64() == VirtualKeyReturn &&
                    _isImeComposing)
                {
                    _imeEnterIsDown = true;
                }

                base.WndProc(ref message);

                if (message.Msg == WmImeEndComposition)
                {
                    _isImeComposing = false;
                    _suppressEnterUntilKeyUp = _imeEnterIsDown;
                }
            }

            private bool TryHandleEnter(Keys keyData)
            {
                if ((keyData & Keys.KeyCode) != Keys.Enter)
                {
                    return false;
                }

                Keys modifiers = keyData & Keys.Modifiers;
                if (_isImeComposing || _suppressEnterUntilKeyUp)
                {
                    return false;
                }

                if (modifiers != Keys.None && modifiers != Keys.Shift)
                {
                    return false;
                }

                if (_handledEnterIsDown)
                {
                    return true;
                }

                _handledEnterIsDown = true;
                if (modifiers == Keys.None)
                {
                    SendRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                if (modifiers == Keys.Shift)
                {
                    SelectedText = System.Environment.NewLine;
                    return true;
                }

                return false;
            }

            private void ResetEnterState()
            {
                _handledEnterIsDown = false;
                _imeEnterIsDown = false;
                _suppressEnterUntilKeyUp = false;
            }
        }

        private sealed class SurfacePanel : Panel
        {
            private Color _borderColor = TaskPaneControl.BorderColor;
            private int _cornerRadius = 8;

            public SurfacePanel()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
            }

            public Color BorderColor
            {
                get => _borderColor;
                set
                {
                    if (_borderColor == value)
                    {
                        return;
                    }

                    _borderColor = value;
                    Invalidate();
                }
            }

            public int CornerRadius
            {
                get => _cornerRadius;
                set
                {
                    int nextValue = Math.Max(0, value);
                    if (_cornerRadius == nextValue)
                    {
                        return;
                    }

                    _cornerRadius = nextValue;
                    Invalidate();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Color outerColor = Parent?.BackColor ?? CanvasColor;
                e.Graphics.Clear(outerColor);
                if (Width <= 1 || Height <= 1)
                {
                    return;
                }

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                RectangleF bounds = new RectangleF(0.5F, 0.5F, Math.Max(0, Width - 1F), Math.Max(0, Height - 1F));
                using (GraphicsPath path = CreateRoundedPath(bounds, CornerRadius))
                using (var brush = new SolidBrush(BackColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width <= 1 || Height <= 1)
                {
                    return;
                }

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
                using (GraphicsPath path = CreateRoundedPath(bounds, CornerRadius))
                using (var pen = new Pen(BorderColor, 1F))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            private static GraphicsPath CreateRoundedPath(RectangleF bounds, int radius)
            {
                var path = new GraphicsPath();
                float diameter = Math.Min(
                    Math.Max(0F, radius * 2F),
                    Math.Min(bounds.Width, bounds.Height));
                if (diameter <= 1F)
                {
                    path.AddRectangle(bounds);
                    return path;
                }

                var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
                path.AddArc(arc, 180F, 90F);
                arc.X = bounds.Right - diameter;
                path.AddArc(arc, 270F, 90F);
                arc.Y = bounds.Bottom - diameter;
                path.AddArc(arc, 0F, 90F);
                arc.X = bounds.X;
                path.AddArc(arc, 90F, 90F);
                path.CloseFigure();
                return path;
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
