using System.Runtime.InteropServices;

namespace IDKEngine.IntelOpenImageDenoise;

public static unsafe partial class OIDN
{
    public const string LIBRARY_NAME = "OpenImageDenoise"; // https://github.com/RenderKit/oidn

    public static readonly bool LibraryFound = TryFindLibrary();

    private static bool TryFindLibrary()
    {
        if (NativeLibrary.TryLoad(LIBRARY_NAME, out nint handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }

        return false;
    }

    public enum DeviceType : int
    {
        DEFAULT = 0, // select device automatically

        CPU = 1, // CPU device
        SYCL = 2, // SYCL device
        CUDA = 3, // CUDA device
        HIP = 4, // HIP device
        METAL = 5, // Metal device
    }

    public enum Error : int
    {
        NONE = 0, // no error occurred
        UNKNOWN = 1, // an unknown error occurred
        INVALID_ARGUMENT = 2, // an invalid argument was specified
        INVALID_OPERATION = 3, // the operation is not allowed
        OUT_OF_MEMORY = 4, // not enough memory to execute the operation
        UNSUPPORTED_HARDWARE = 5, // the hardware (e.g. CPU) is not supported
        CANCELLED = 6, // the operation was cancelled by the user
    }

    public enum Format : int
    {
        UNDEFINED = 0,

        // 32-bit single-precision floating-point scalar and vector formats
        FLOAT = 1,
        FLOAT2,
        FLOAT3,
        FLOAT4,

        // 16-bit half-precision floating-point scalar and vector formats
        HALF = 257,
        HALF2,
        HALF3,
        HALF4,
    }

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnNewDevice")]
    public static partial void* NewDevice(DeviceType type);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnCommitDevice")]
    public static partial void CommitDevice(void* device);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnNewBuffer")]
    public static partial void* NewBuffer(void* device, nint byteSize);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnNewFilter")]
    public static partial void* NewFilter(void* device, byte* type);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnCommitFilter")]
    public static partial void* CommitFilter(void* filter);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnSetFilterBool")]
    public static partial void SetFilterBool(void* filter, byte* name, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnGetBufferData")]
    public static partial void* GetBufferData(void* buffer);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnExecuteFilter")]
    public static partial void ExecuteFilter(void* filter);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnSetFilterImage")]
    public static partial void SetFilterImage(void* filter, byte* name, void* buffer, Format format, nint width, nint height, nint byteOffset, nint pixelByteStride, nint rowByteStride);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnReleaseBuffer")]
    public static partial void ReleaseBuffer(void* buffer);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnReleaseFilter")]
    public static partial void ReleaseFilter(void* filter);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnReleaseDevice")]
    public static partial void ReleaseDevice(void* device);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnSetDeviceErrorFunction")]
    public static partial void SetDeviceErrorFunction(void* device, delegate* unmanaged<void*, Error, byte*, void> func, void* userPtr);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnGetDeviceError")]
    public static partial Error GetDeviceError(void* device, out byte* outMessage);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnGetNumPhysicalDevices")]
    public static partial int GetNumPhysicalDevices();

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnGetDeviceInt")]
    public static partial int GetDeviceInt(void* device, byte* name);

    [LibraryImport(LIBRARY_NAME, EntryPoint = "oidnSetDeviceInt")]
    public static partial void SetDeviceInt(void* device, byte* name, int value);
}
