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
			using MemoryStack sp = MemoryStack.Push();
			var adapterFactory = DXCore.CreateAdapterFactory<IDXCoreAdapterFactory>();
			var adapterAttribs = sp.Values(DXCore.AttributeD3D12Graphics);
			var adapterList = COMHelpers.GetObjectFromCOMGetter<IDXCoreAdapterList>((in Guid riid) => adapterFactory.CreateAdapterList((uint)adapterAttribs.ArraySize, adapterAttribs.Ptr, riid))!;
			Console.WriteLine($"[DXCore Raw] # of adapters: {adapterList.GetAdapterCount()}");

			Span<bool> pHwd = stackalloc bool[1];

			uint nadapters = adapterList.GetAdapterCount();
			for (uint i = 0; i < nadapters; i++) {
				Console.WriteLine($"[DXCore Raw] Adapter #{i}");
				var adapter = COMHelpers.GetObjectFromCOMGetter<IDXCoreAdapter>((in Guid riid) => adapterList.GetAdapter(i, riid))!;
				if (adapter.IsPropertySupported(DXCoreAdapterProperty.DriverDescription)) {
					nuint szDesc = adapter.GetPropertySize(DXCoreAdapterProperty.DriverDescription);
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
