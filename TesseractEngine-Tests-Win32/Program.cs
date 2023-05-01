using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract.Core.Native;
using Tesseract.DirectX.Core;
using Tesseract.Windows;

namespace Tesseract.Tests {

	public static class TestWin32 {

		public static void Main(string[] _) {
			Console.WriteLine("Testing DXCore Raw...");
			TestDXCore();
		}

		private static void TestDXCore() {
			var adapterFactory = DXCore.CreateAdapterFactory<IDXCoreAdapterFactory>();
			var adapterList = adapterFactory.CreateAdapterList<IDXCoreAdapterList>(stackalloc Guid[] { DXCore.AttributeD3D12Graphics });
			Console.WriteLine($"[DXCore Raw] # of adapters: {adapterList.GetAdapterCount()}");

			Span<bool> pHwd = stackalloc bool[1];

			uint nadapters = adapterList.GetAdapterCount();
			for (uint i = 0; i < nadapters; i++) {
				Console.WriteLine($"[DXCore Raw] Adapter #{i}");
				var adapter = adapterList.GetAdapter<IDXCoreAdapter>((int)i);
				if (adapter.IsPropertySupported(DXCoreAdapterProperty.DriverDescription)) {
					adapter.GetPropertySize(DXCoreAdapterProperty.DriverDescription, out nuint szDesc);
					using ManagedPointer<byte> pDesc = new((int)szDesc);
					adapter.GetProperty(DXCoreAdapterProperty.DriverDescription, (uint)pDesc.ArraySize, pDesc.Ptr);
					Console.WriteLine($"[DXCore Raw]     DriverDescription={MemoryUtil.GetUTF8(pDesc.Span)}");
				}
				if (adapter.IsPropertySupported(DXCoreAdapterProperty.IsHardware)) {
					Console.WriteLine($"[DXCore Raw]     IsHardware={adapter.GetProperty<bool>(DXCoreAdapterProperty.IsHardware)}");
				}
				if (adapter.IsPropertySupported(DXCoreAdapterProperty.DedicatedAdapterMemory)) {
					Console.WriteLine($"[DXCore Raw]     DedicatedAdapterMemory={adapter.GetProperty<ulong>(DXCoreAdapterProperty.DedicatedAdapterMemory)}");
				}
				adapter.Release();
			}

			adapterList.Release();
			adapterFactory.Release();
		}

	}

}
