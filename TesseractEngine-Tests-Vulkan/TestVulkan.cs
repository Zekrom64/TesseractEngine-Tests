using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Tesseract.Core.Math;
using Tesseract.Core.Native;
using Tesseract.Core.Util;
using Tesseract.Tests.GLFW;
using Tesseract.GLFW;
using Tesseract.Vulkan;

namespace Tesseract.Tests.Vulkan {
	
	public static class TestVulkan {

		private class GLFWVulkanLoader : IVKLoader {

			public IntPtr GetVKProcAddress(string name) {
				return GLFW3.Functions.glfwGetInstanceProcAddress(IntPtr.Zero, name);
			}

		}

		private static VKInstance CreateInstance(VK vk) {
			using MemoryStack sp = MemoryStack.Push();
			using ManagedPointer<VKApplicationInfo> pAppInfo = new(new VKApplicationInfo() {
				Type = VKStructureType.ApplicationInfo,
				APIVersion = VK10.ApiVersion,
				ApplicationName = "TesseractEngine-Test",
				ApplicationVersion = VK10.MakeVersion(0, 1, 0),
				EngineName = "Tesseract",
				EngineVersion = VK10.MakeVersion(0, 1, 0)
			});

			HashSet<string> layers = new(), extensions = new();
			extensions.Add(KHRSurface.ExtensionName);
			extensions.Add(EXTDebugReport.ExtensionName);
			layers.Add("VK_LAYER_KHRONOS_validation");
			//layers.Add("VK_LAYER_LUNARG_api_dump");

			foreach (string ext in GLFW3.RequiredInstanceExtensions) extensions.Add(ext);

			return vk.CreateInstance(new VKInstanceCreateInfo() {
				Type = VKStructureType.InstanceCreateInfo,
				ApplicationInfo = pAppInfo,
				EnabledLayerCount = (uint)layers.Count,
				EnabledLayerNames = sp.UTF8Array(layers),
				EnabledExtensionCount = (uint)extensions.Count,
				EnabledExtensionNames = sp.UTF8Array(extensions)
			});
		}

		private static VKSurfaceKHR CreateSurface(VKInstance instance, GLFWWindow window) {
			VK.CheckError((VKResult)GLFW3.Functions.glfwCreateWindowSurface(instance, window.Window, IntPtr.Zero, out ulong surface), "Failed to create window surface");
			return new VKSurfaceKHR(instance, surface, null);
		}

		private static VKDevice CreateDevice(VKInstance instance, VKSurfaceKHR surface, out VKPhysicalDevice physicalDevice, out VKQueue queue, out int queueFamily) {
			using MemoryStack sp = MemoryStack.Push();

			physicalDevice = instance.PhysicalDevices[0];

			var physicalDeviceProperties = physicalDevice.Properties;

			Console.WriteLine($"[Vulkan] Physical Device \"{physicalDeviceProperties.DeviceName}\"");

			queueFamily = -1;
			var queueFamilies = physicalDevice.QueueFamilyProperties;
			for (int family = 0; family < queueFamilies.Length; family++) {
				var queueInfo = queueFamilies[family];
				if ((queueInfo.QueueFlags & VKQueueFlagBits.Graphics) != 0 && physicalDevice.GetSurfaceSupportKHR((uint)family, surface)) {
					queueFamily = family;
					break;
				}
			}

			if (queueFamily == -1) throw new VulkanException("Physical device does not have graphics queue which supports surface!");

			HashSet<string> extensions = new();
			extensions.Add(KHRSwapchain.ExtensionName);

			VKDevice device = physicalDevice.CreateDevice(new VKDeviceCreateInfo() {
				Type = VKStructureType.DeviceCreateInfo,
				QueueCreateInfoCount = 1,
				QueueCreateInfos = sp.Values(new VKDeviceQueueCreateInfo() {
					Type = VKStructureType.DeviceQueueCreateInfo,
					QueueCount = 1,
					QueueFamilyIndex = (uint)queueFamily,
					QueuePriorities = sp.Values(1.0f)
				}),
				EnabledExtensionCount = (uint)extensions.Count,
				EnabledExtensionNames = sp.UTF8Array(extensions)
			});

			queue = device.GetQueue((uint)queueFamily, 0);

			return device;
		}

		private static int ScoreFormat(VKSurfaceFormatKHR format) {
			int score = 0;
			if (format.ColorSpace == VKColorSpaceKHR.SRGBNonlinear) score += 1;
			if (format.Format == VKFormat.R8G8B8A8UNorm) score += 1;
			return score;
		}

		private static int ScorePresentMode(VKPresentModeKHR mode) => mode switch {
			VKPresentModeKHR.Immediate => -10,
			VKPresentModeKHR.FIFO => 5,
			VKPresentModeKHR.FIFORelaxed => 10,
			VKPresentModeKHR.Mailbox => 20,
			_ => 0
		};

		private static VKSwapchainKHR CreateSwapchain(VKPhysicalDevice physicalDevice, int queueFamily, VKDevice device, VKSurfaceKHR surface, GLFWWindow window, out VKFormat imageFormat) {
			using MemoryStack sp = MemoryStack.Push();

			VKSurfaceCapabilitiesKHR capabilities = physicalDevice.GetSurfaceCapabilitiesKHR(surface);
			VKSurfaceFormatKHR[] formats = physicalDevice.GetSurfaceFormatsKHR(surface);
			VKPresentModeKHR[] presentModes = physicalDevice.GetSurfacePresentModesKHR(surface);

			Vector2ui extent = (Vector2ui)window.FramebufferSize;
			extent.X = Math.Min(Math.Max(extent.X, capabilities.MinImageExtent.X), capabilities.MaxImageExtent.X);
			extent.Y = Math.Min(Math.Max(extent.X, capabilities.MinImageExtent.Y), capabilities.MaxImageExtent.Y);

			uint imageCount = Math.Min(Math.Max(2, capabilities.MinImageCount), capabilities.MaxImageCount);

			VKSurfaceFormatKHR format = (from fmt in formats
										 let score = ScoreFormat(fmt)
										 orderby score descending
										 select fmt).First();

			VKPresentModeKHR presentMode = (from mode in presentModes
											let score = ScorePresentMode(mode)
											orderby score descending
											select mode).First();

			imageFormat = format.Format;

			return device.CreateSwapchainKHR(new VKSwapchainCreateInfoKHR() {
				Type = VKStructureType.SWAPCHAIN_CREATE_INFO_KHR,
				Surface = surface,
				ImageExtent = extent,
				ImageArrayLayers = 1,
				ImageFormat = format.Format,
				ImageSharingMode = VKSharingMode.Exclusive,
				ImageColorSpace = format.ColorSpace,
				ImageUsage = VKImageUsageFlagBits.TransferDst,
				MinImageCount = imageCount,
				CompositeAlpha = VKCompositeAlphaFlagBitsKHR.Opaque,
				Clipped = true,
				PreTransform = capabilities.CurrentTransform,
				PresentMode = presentMode
			});
		}

		public static void TestRaw() => TestGLFW.RunWithGLFW(() => {
			GLFW3.DefaultWindowHints();
			GLFW3.WindowHint(GLFWWindowAttrib.Resizable, 0);
			GLFW3.WindowHint(GLFWWindowAttrib.ClientAPI, (int)GLFWClientAPI.NoAPI);
			GLFWWindow window = new(new Vector2i(800, 600), "Test Vulkan");

			VK vk = new(new GLFWVulkanLoader());
			using VKInstance instance = CreateInstance(vk);
			using VKDebugReportCallbackEXT debugReportCallback = instance.CreateDebugReportCallbackEXT(new VKDebugReportCallbackCreateInfoEXT() {
				Type = VKStructureType.DEBUG_REPORT_CALLBACK_CREATE_INFO_EXT,
				Flags = VKDebugReportFlagBitsEXT.Error | VKDebugReportFlagBitsEXT.Warning | VKDebugReportFlagBitsEXT.PerformanceWarning,
				Callback = (VKDebugReportFlagBitsEXT flags, VKDebugReportObjectTypeEXT objectType, ulong obj, nuint location, int messageCode, string layerPrefix, string message, IntPtr userData) => {
					if ((flags & VKDebugReportFlagBitsEXT.Error) != 0) Console.Error.WriteLine($"[Vulkan][{layerPrefix}]: {message}");
					else Console.WriteLine($"[Vulkan][{layerPrefix}]: {message}");
					return false;
				}
			});
			using VKSurfaceKHR surface = CreateSurface(instance, window);

			using VKDevice device = CreateDevice(instance, surface, out VKPhysicalDevice physicalDevice, out VKQueue queue, out int queueFamily);
			using VKSwapchainKHR swapchain = CreateSwapchain(physicalDevice, queueFamily, device, surface, window, out VKFormat swapchainImageFormat);

			while(!window.ShouldClose) {
				GLFW3.PollEvents();
			}

			window.Dispose();
		});

	}

}
