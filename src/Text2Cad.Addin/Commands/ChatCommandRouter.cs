using System;
using System.Collections.Generic;

namespace Text2Cad.Addin.Commands
{
    internal sealed class ChatRouteResult
    {
        private ChatRouteResult(
            PrimitiveKind? primitive,
            string assistantMessage,
            string busyMessage)
        {
            Primitive = primitive;
            AssistantMessage = assistantMessage;
            BusyMessage = busyMessage;
        }

        public PrimitiveKind? Primitive { get; }

        public string AssistantMessage { get; }

        public string BusyMessage { get; }

        public bool ShouldExecute => Primitive.HasValue;

        public static ChatRouteResult Execute(
            PrimitiveKind primitive,
            string assistantMessage,
            string busyMessage)
        {
            return new ChatRouteResult(primitive, assistantMessage, busyMessage);
        }

        public static ChatRouteResult Reply(string assistantMessage)
        {
            return new ChatRouteResult(null, assistantMessage, string.Empty);
        }
    }

    internal static class ChatCommandRouter
    {
        public const string SupportedCapabilities =
            "当前演示支持：创建 100 × 60 × 10 mm 测试板、创建 40 mm 方块、创建 Ø40 × 60 mm 圆柱。";

        public static ChatRouteResult Route(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return ChatRouteResult.Reply("请输入一条建模指令。");
            }

            string normalized = input.Trim().ToLowerInvariant();
            string compact = normalized.Replace(" ", string.Empty);
            var matches = new List<PrimitiveKind>();

            if (ContainsAny(normalized, "测试板", "安装板", "板件", "plate") ||
                ContainsAny(compact, "100×60", "100x60"))
            {
                matches.Add(PrimitiveKind.TestPlate);
            }

            if (ContainsAny(normalized, "方块", "立方体", "正方体", "cube") ||
                ContainsAny(compact, "40×40×40", "40x40x40"))
            {
                matches.Add(PrimitiveKind.Cube);
            }

            if (ContainsAny(normalized, "圆柱", "cylinder", "ø40", "直径40"))
            {
                matches.Add(PrimitiveKind.Cylinder);
            }

            if (matches.Count > 1)
            {
                return ChatRouteResult.Reply(
                    "这版一次只执行一个固定能力。请把多个形状拆成多条消息发送。\n" +
                    SupportedCapabilities);
            }

            if (matches.Count == 1)
            {
                switch (matches[0])
                {
                    case PrimitiveKind.TestPlate:
                        return ChatRouteResult.Execute(
                            PrimitiveKind.TestPlate,
                            "明白，我会新建一个 100 × 60 × 10 mm 的测试板。",
                            "正在调用 SOLIDWORKS API 创建测试板…");
                    case PrimitiveKind.Cube:
                        return ChatRouteResult.Execute(
                            PrimitiveKind.Cube,
                            "明白，我会新建一个边长 40 mm 的方块。",
                            "正在调用 SOLIDWORKS API 创建方块…");
                    case PrimitiveKind.Cylinder:
                        return ChatRouteResult.Execute(
                            PrimitiveKind.Cylinder,
                            "明白，我会新建一个直径 40 mm、高 60 mm 的圆柱。",
                            "正在调用 SOLIDWORKS API 创建圆柱…");
                }
            }

            if (ContainsAny(normalized, "帮助", "能做什么", "支持什么", "help"))
            {
                return ChatRouteResult.Reply(SupportedCapabilities);
            }

            return ChatRouteResult.Reply(
                "我还没有真正的意图识别，目前只能匹配三类固定指令。\n" +
                SupportedCapabilities);
        }

        private static bool ContainsAny(string text, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
