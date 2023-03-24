using System;
using System.Collections.Generic;

namespace Tesseract.Tests {
	static class Program {

		private struct Test {

			public Action TestFunc;

			public string Name;

		}

		static void Main(string[] _) {
			// OBS does implicit layer nonsense that makes the validation layers sad, even for our valid Vulkan 1.0 test
			Environment.SetEnvironmentVariable("DISABLE_VULKAN_OBS_CAPTURE", "1");

			List<Test> tests = new() {
				new() { TestFunc = TestGLFW.TestRaw, Name = "GLFW - Raw API" },
				new() { TestFunc = TestGLFW.TestServices, Name = "GLFW - Services" },

				new() { TestFunc = TestSDL.TestRaw, Name = "SDL2 - Raw API" },

				new() { TestFunc = TestGL.TestRaw, Name = "OpenGL - Raw API" },

				new() { TestFunc = TestVulkan.TestRaw, Name = "Vulkan - Raw API" },

				new() { TestFunc = TestImGui.TestSDL, Name = "ImGui - SDL2 + SDLRenderer" },
				new() { TestFunc = TestImGui.TestGL45, Name = "ImGui - SDL2 + OpenGL 4.5" },

				new() { TestFunc = TestCore.TestCoreGraphicsVulkan, Name = "Core Graphics - Vulkan" },
				new() { TestFunc = TestCore.TestCoreGraphicsGL, Name = "Core Graphics - OpenGL" },
				new() { TestFunc = TestCore.TestCoreImGuiSDLVulkan, Name = "ImGui - Core - Vulkan + SDL" },
				new() { TestFunc = TestCore.TestCoreImGuiSDLGL, Name = "ImGui - Core - OpenGL + SDL" }
			};
			int ntests = 0;
			int successes = 0;
			foreach(Test test in tests) {
				Console.WriteLine($"Next test is \"{test.Name}\" ");
				Console.WriteLine("Press tab to skip or enter to continue...");
				ConsoleKeyInfo keyInfo;
				bool doTest = true;
				do {
					keyInfo = Console.ReadKey(true);
					if (keyInfo.Key == ConsoleKey.Tab) {
						doTest = false;
						break;
					}
				} while (keyInfo.Key != ConsoleKey.Enter);
				if (doTest) {
					ntests++;
					try {
						test.TestFunc();
						successes++;
					} catch (Exception e) {
						Console.Error.WriteLine($"Test \"{test.Name}\" failed!");
						Console.Error.WriteLine(e);
					}
				}
			}
			Console.WriteLine($"{successes}/{ntests} tests succeeeded");
		}
	}
}
