using System;
using System.Text;
using Text2Cad.Addin.Commands;

namespace Text2Cad.Addin.SmokeTests
{
    internal static class Program
    {
        private const double Tolerance = 1e-12;
        private static int _checks;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                VerifyRoutes();
                VerifyCatalog();
                Console.WriteLine($"PASS: {_checks} checks completed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception.Message);
                return 1;
            }
        }

        private static void VerifyRoutes()
        {
            AssertRoute("创建一个 100 × 60 × 10 mm 测试板", PrimitiveKind.TestPlate);
            AssertRoute("create a plate", PrimitiveKind.TestPlate);
            AssertRoute("创建一个边长 40 mm 的方块", PrimitiveKind.Cube);
            AssertRoute("make a cube", PrimitiveKind.Cube);
            AssertRoute("创建一个直径 40 mm、高 60 mm 的圆柱", PrimitiveKind.Cylinder);
            AssertRoute("make a cylinder", PrimitiveKind.Cylinder);

            AssertReply("你能做什么", "当前演示支持");
            AssertReply("做一个球", "没有真正的意图识别");
            AssertReply("创建测试板和圆柱", "一次只执行一个固定能力");
            AssertReply(string.Empty, "请输入一条建模指令");
        }

        private static void VerifyCatalog()
        {
            PrimitiveDefinition plate = PrimitiveCatalog.Get(PrimitiveKind.TestPlate);
            Check(plate.Kind == PrimitiveKind.TestPlate, "plate kind");
            Check(plate.Profile == PrimitiveProfileKind.Rectangle, "plate profile");
            CheckNear(0.050, plate.HalfWidthMeters, "plate half width");
            CheckNear(0.030, plate.HalfHeightMeters, "plate half height");
            CheckNear(0.010, plate.ExtrusionDepthMeters, "plate depth");
            Check(plate.SketchFeatureName == "Text2CAD_TestPlate_Sketch", "plate sketch name");
            Check(plate.ExtrusionFeatureName == "Text2CAD_TestPlate_100x60x10", "plate feature name");

            PrimitiveDefinition cube = PrimitiveCatalog.Get(PrimitiveKind.Cube);
            Check(cube.Kind == PrimitiveKind.Cube, "cube kind");
            Check(cube.Profile == PrimitiveProfileKind.Rectangle, "cube profile");
            CheckNear(0.020, cube.HalfWidthMeters, "cube half width");
            CheckNear(0.020, cube.HalfHeightMeters, "cube half height");
            CheckNear(0.040, cube.ExtrusionDepthMeters, "cube depth");
            Check(cube.ExtrusionFeatureName == "Text2CAD_Cube_40x40x40", "cube feature name");

            PrimitiveDefinition cylinder = PrimitiveCatalog.Get(PrimitiveKind.Cylinder);
            Check(cylinder.Kind == PrimitiveKind.Cylinder, "cylinder kind");
            Check(cylinder.Profile == PrimitiveProfileKind.Circle, "cylinder profile");
            CheckNear(0.020, cylinder.RadiusMeters, "cylinder radius");
            CheckNear(0.060, cylinder.ExtrusionDepthMeters, "cylinder depth");
            Check(cylinder.SketchFeatureName == "Text2CAD_Cylinder_Sketch", "cylinder sketch name");
            Check(cylinder.ExtrusionFeatureName == "Text2CAD_Cylinder_D40x60", "cylinder feature name");

            bool invalidKindRejected = false;
            try
            {
                PrimitiveCatalog.Get((PrimitiveKind)999);
            }
            catch (ArgumentOutOfRangeException)
            {
                invalidKindRejected = true;
            }

            Check(invalidKindRejected, "invalid primitive rejected");
        }

        private static void AssertRoute(string prompt, PrimitiveKind expected)
        {
            ChatRouteResult route = ChatCommandRouter.Route(prompt);
            Check(route.ShouldExecute, $"route executes: {prompt}");
            Check(route.Primitive == expected, $"route target: {prompt}");
            Check(!string.IsNullOrWhiteSpace(route.AssistantMessage), $"route acknowledgement: {prompt}");
            Check(!string.IsNullOrWhiteSpace(route.BusyMessage), $"route busy message: {prompt}");
        }

        private static void AssertReply(string prompt, string expectedFragment)
        {
            ChatRouteResult route = ChatCommandRouter.Route(prompt);
            Check(!route.ShouldExecute, $"reply does not execute: {prompt}");
            Check(route.AssistantMessage.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                $"reply text: {prompt}");
        }

        private static void CheckNear(double expected, double actual, string name)
        {
            Check(Math.Abs(expected - actual) <= Tolerance, name);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Check failed: " + name);
            }

            _checks++;
        }
    }
}
