using System;
using System.Drawing;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SwCopilot.Execution;
using SwCopilot.Planning;
using SwCopilot.Routing;

namespace SwCopilot.UI
{
    /// <summary>
    /// The Task Pane chat UI (spec section 8). Hand-built WinForms (no .Designer file)
    /// so it is easy to read and safe to host via ITaskpaneView.DisplayWindowFromHandlex64.
    /// All SolidWorks work runs synchronously on this (UI) thread.
    ///
    /// Layout rule to avoid WinForms docking ambiguity: never dock two controls to the
    /// same edge of one parent. Each parent has at most one control per edge plus a
    /// single Fill control, and the Fill control is always added LAST.
    /// </summary>
    public class CopilotControl : UserControl
    {
        private const int InputHeight = 44;
        private const int PlanHeight = 150;

        private readonly ISldWorks _app;
        private CommandBase _pending;

        private static readonly Color UserColor = Color.FromArgb(215, 236, 255);
        private static readonly Color AssistantColor = Color.FromArgb(238, 238, 238);
        private static readonly Color SuccessColor = Color.FromArgb(219, 244, 222);
        private static readonly Color ErrorColor = Color.FromArgb(250, 220, 220);

        private Label _status;
        private FlowLayoutPanel _transcript;
        private Panel _bottomStack;
        private Panel _planPanel;
        private Label _planSteps;
        private Button _btnExecute;
        private Button _btnCancel;
        private TextBox _input;
        private Button _btnSend;

        public CopilotControl(ISldWorks app)
        {
            _app = app;
            InitializeComponent();
            RefreshStatus();
            AddBubble("助手",
                "你好,我是 SolidWorks Copilot (MVP)。试试:\n" +
                "· 创建一个 100x60x8 的安装板，四个 M6 通孔\n" +
                "· 把厚度改成 10mm\n" +
                "· 把孔径改成 8mm",
                AssistantColor);
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // Layout note: for each parent we set Dock, then call BringToFront() on the
            // Fill child so it reliably takes the space the edge-docked controls leave
            // (WinForms docking is z-order driven; BringToFront is the dependable fix).

            // ---- header (Top edge of the control) ----
            Panel header = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(8, 6, 8, 4) };
            Label title = new Label
            {
                Text = "SolidWorks Copilot MVP",
                Dock = DockStyle.Top,
                Height = 24,
                AutoSize = false,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold)
            };
            _status = new Label { Text = "文档:—", Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DimGray };
            header.Controls.Add(title);
            header.Controls.Add(_status);
            _status.BringToFront();

            // ---- input row ----
            Panel inputPanel = new Panel { Dock = DockStyle.Bottom, Height = InputHeight, Padding = new Padding(8, 6, 8, 8) };
            _btnSend = new Button { Text = "发送", Dock = DockStyle.Right, Width = 72 };
            _btnSend.Click += OnSend;
            _input = new TextBox { Dock = DockStyle.Fill };
            _input.KeyDown += OnInputKeyDown;
            inputPanel.Controls.Add(_btnSend);
            inputPanel.Controls.Add(_input);
            _input.BringToFront();

            // ---- plan panel (title on top, buttons on bottom, steps fill) ----
            _planPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4), Visible = false, BorderStyle = BorderStyle.FixedSingle };
            Label planTitle = new Label { Text = "执行计划", Dock = DockStyle.Top, Height = 20, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold) };
            FlowLayoutPanel planButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
            _btnExecute = new Button { Text = "执行", Width = 84, BackColor = SuccessColor };
            _btnExecute.Click += OnExecute;
            _btnCancel = new Button { Text = "取消", Width = 84 };
            _btnCancel.Click += OnCancel;
            planButtons.Controls.Add(_btnExecute);  // right-most
            planButtons.Controls.Add(_btnCancel);   // to its left
            _planSteps = new Label { Dock = DockStyle.Fill, AutoSize = false, Text = "" };
            _planPanel.Controls.Add(planTitle);
            _planPanel.Controls.Add(planButtons);
            _planPanel.Controls.Add(_planSteps);
            _planSteps.BringToFront();

            // ---- bottom stack: plan (fill) above input (bottom) ----
            _bottomStack = new Panel { Dock = DockStyle.Bottom, Height = InputHeight };
            _bottomStack.Controls.Add(inputPanel);
            _bottomStack.Controls.Add(_planPanel);
            _planPanel.BringToFront();

            // ---- transcript ----
            _transcript = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(6),
                BackColor = Color.White
            };
            _transcript.Resize += delegate { ReflowBubbles(); };

            Controls.Add(header);
            Controls.Add(_bottomStack);
            Controls.Add(_transcript);
            _transcript.BringToFront();

            MinimumSize = new Size(300, 380);
            Size = new Size(360, 620);
            ResumeLayout(false);
        }

        // ---------- events ----------

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // no ding, no newline
                OnSend(sender, e);
            }
        }

        private void OnSend(object sender, EventArgs e)
        {
            string text = (_input.Text ?? "").Trim();
            if (text.Length == 0) return;
            _input.Clear();
            RefreshStatus();
            AddBubble("你", text, UserColor);

            CommandBase cmd = CommandRouter.Route(text);
            if (cmd.Kind == CommandKind.Unsupported)
            {
                AddBubble("助手", PlanBuilder.UnsupportedMessage(), AssistantColor);
                HidePlan();
                _pending = null;
                return;
            }

            _pending = cmd;
            AddBubble("助手", "已识别命令:" + Describe(cmd), AssistantColor);
            ShowPlan(PlanBuilder.BuildSteps(cmd));
        }

        private void OnExecute(object sender, EventArgs e)
        {
            if (_pending == null) return;
            SetBusy(true);
            AddBubble("助手", "执行中…", AssistantColor);
            _transcript.Update(); // paint the busy bubble before the blocking SW call

            ExecutionResult res;
            try { res = new Executor(_app).Run(_pending); }
            catch (Exception ex) { res = ExecutionResult.Fail("执行异常。", ex.Message); }

            RenderResult(res);
            _pending = null;
            HidePlan();
            SetBusy(false);
            RefreshStatus();
        }

        private void OnCancel(object sender, EventArgs e)
        {
            _pending = null;
            HidePlan();
            AddBubble("助手", "已取消。", AssistantColor);
        }

        // ---------- rendering ----------

        private void RenderResult(ExecutionResult res)
        {
            if (res.Success)
            {
                string msg = "✅ " + res.Message;
                if (res.CreatedFeatures.Count > 0)
                    msg += "\n新建特征:" + string.Join(", ", res.CreatedFeatures.ToArray());
                if (res.ModifiedDimensions.Count > 0)
                    msg += "\n修改尺寸:" + string.Join(", ", res.ModifiedDimensions.ToArray());
                AddBubble("助手", msg, SuccessColor);
            }
            else
            {
                string msg = "❌ " + res.Message;
                if (!string.IsNullOrEmpty(res.Error))
                    msg += "\n原因:" + res.Error;
                AddBubble("助手", msg, ErrorColor);
            }
        }

        private void ShowPlan(string[] steps)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < steps.Length; i++)
                sb.AppendLine((i + 1) + ". " + steps[i]);
            _planSteps.Text = sb.ToString();
            _planPanel.Visible = true;
            _bottomStack.Height = PlanHeight + InputHeight;
            _btnExecute.Enabled = true;
        }

        private void HidePlan()
        {
            _planPanel.Visible = false;
            _bottomStack.Height = InputHeight;
        }

        private void AddBubble(string role, string text, Color back)
        {
            Label lbl = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(BubbleWidth(), 0),
                Text = role + ": " + text,
                Padding = new Padding(8),
                Margin = new Padding(4),
                BackColor = back
            };
            _transcript.Controls.Add(lbl);
            _transcript.ScrollControlIntoView(lbl);
        }

        private void ReflowBubbles()
        {
            int w = BubbleWidth();
            foreach (Control c in _transcript.Controls)
                c.MaximumSize = new Size(w, 0);
        }

        private int BubbleWidth()
        {
            int w = _transcript.ClientSize.Width - 30;
            return w < 80 ? 80 : w;
        }

        private void SetBusy(bool busy)
        {
            _input.Enabled = !busy;
            _btnSend.Enabled = !busy;
            _btnExecute.Enabled = !busy;
            _btnCancel.Enabled = !busy;
        }

        private void RefreshStatus()
        {
            try { _status.Text = "文档:" + SwApiHelpers.DescribeActiveDoc(_app); }
            catch { _status.Text = "文档:—"; }
        }

        private static string Describe(CommandBase cmd)
        {
            var a = cmd as CreateMountingPlateCommand; if (a != null) return a.Summary;
            var b = cmd as UpdateThicknessCommand; if (b != null) return b.Summary;
            var c = cmd as UpdateHoleDiameterCommand; if (c != null) return c.Summary;
            return cmd.Kind.ToString();
        }
    }
}
