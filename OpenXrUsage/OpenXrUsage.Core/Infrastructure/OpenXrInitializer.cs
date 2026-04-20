using MonoGame.Framework.Utilities;
using Silk.NET.OpenXR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenXrUsage.Core.Infrastructure;

public static unsafe class OpenXrInitializer
{
    public static bool TryCreateInstance(
        Silk.NET.OpenXR.XR xr,
        GraphicsBackend backend,
        out Instance instance,
        out string runtimeName,
        out bool hasHandTracking,
        out bool hasHandInteraction)
    {
        instance = default;
        runtimeName = "Unknown";
        hasHandTracking = false;
        hasHandInteraction = false;

        var appInfo = new ApplicationInfo
        {
            ApplicationVersion = 1,
            EngineVersion = 1,
            // OpenXR uses 64-bit versioning: (major << 48) | (minor << 32) | patch
            ApiVersion = (1UL << 48) | (0UL << 32) | 0UL
        };

        fixed (byte* appNamePtr = "Legate\0"u8.ToArray())
        fixed (byte* engineNamePtr = "Entropia3D\0"u8.ToArray())
        {
            for (int i = 0; i < 128 && appNamePtr[i] != 0; i++)
            {
                appInfo.ApplicationName[i] = appNamePtr[i];
            }
            for (int i = 0; i < 128 && engineNamePtr[i] != 0; i++)
            {
                appInfo.EngineName[i] = engineNamePtr[i];
            }
        }

        // Enumerate supported instance extensions
        uint extCount = 0;
        xr.EnumerateInstanceExtensionProperties((byte*)null, 0, &extCount, null);
        var extensionProps = new ExtensionProperties[extCount];
        for (int i = 0; i < extCount; i++)
        {
            extensionProps[i].Type = StructureType.ExtensionProperties;
            extensionProps[i].Next = null;
        }
        fixed (ExtensionProperties* propsPtr = extensionProps)
        {
            xr.EnumerateInstanceExtensionProperties((byte*)null, extCount, &extCount, propsPtr);
        }

        var availableExtensions = new List<string>((int)extCount);
        for (int i = 0; i < extCount; i++)
        {
            string extName;
            fixed (byte* pName = extensionProps[i].ExtensionName)
            {
                extName = Marshal.PtrToStringAnsi((IntPtr)pName) ?? string.Empty;

                availableExtensions.Add(extName);
            }
            if (extName == "XR_EXT_hand_tracking")
            {
                hasHandTracking = true;
            }
            if (extName == "XR_EXT_hand_interaction")
            {
                hasHandInteraction = true;
            }
        }

        var cse = string.Join(',', availableExtensions.Order());
        Trace.TraceInformation($"[{nameof(OpenXrInitializer)}] Available OpenXR Instance Extensions: {cse}");

        // Select the graphics binding extension based on the active backend
        byte[] graphicsExtensionBytes = backend switch
        {
            GraphicsBackend.DirectX12 => Encoding.UTF8.GetBytes("XR_KHR_D3D12_enable\0"),
            GraphicsBackend.Vulkan => Encoding.UTF8.GetBytes("XR_KHR_vulkan_enable\0"),
            _ => throw new NotSupportedException($"Unsupported graphics backend: {backend}")
        };
        var handTrackingExtension = "XR_EXT_hand_tracking"u8;
        var handInteractionExtension = "XR_EXT_hand_interaction"u8;

        fixed (byte* graphicsExtPtr = graphicsExtensionBytes,
                     handTrackingExtensionPtr = handTrackingExtension,
                     handInteractionExtensionPtr = handInteractionExtension)
        {
            byte*[] extensions = new byte*[3];
            int extensionCount = 0;

            extensions[extensionCount++] = graphicsExtPtr;
            if (hasHandTracking)
            {
                extensions[extensionCount++] = handTrackingExtensionPtr;
            }
            if (hasHandInteraction)
            {
                extensions[extensionCount++] = handInteractionExtensionPtr;
            }

            fixed (byte** extensionsPtr = extensions)
            {
                var createInfo = new InstanceCreateInfo
                {
                    Type = StructureType.InstanceCreateInfo,
                    Next = null,
                    CreateFlags = 0,
                    ApplicationInfo = appInfo,
                    EnabledApiLayerCount = 0,
                    EnabledApiLayerNames = null,
                    EnabledExtensionCount = (uint)extensionCount,
                    EnabledExtensionNames = extensionsPtr
                };

                Instance localInstance;
                var res = xr.CreateInstance(&createInfo, &localInstance);
                if (res != Result.Success)
                {
                    Trace.WriteLine($"[{nameof(OpenXrInitializer)}] xrCreateInstance failed: {res}");
                    return false;
                }
                instance = localInstance;
            }
        }

        var instanceProps = new InstanceProperties
        {
            Type = StructureType.InstanceProperties,
            Next = null
        };
        xr.GetInstanceProperties(instance, &instanceProps);

        string rawRuntimeName = Marshal.PtrToStringAnsi((IntPtr)instanceProps.RuntimeName) ?? "Unknown";
        ulong v = instanceProps.RuntimeVersion;
        uint major = (uint)(v >> 48);
        uint minor = (uint)((v >> 32) & 0xFFFF);
        uint patch = (uint)(v & 0xFFFFFFFF);
        runtimeName = $"{rawRuntimeName} {major}.{minor}.{patch}";

        Trace.WriteLine($"[{nameof(OpenXrInitializer)}] Connected to OpenXR Runtime: {runtimeName}");
        if (!hasHandTracking)
        {
            Trace.WriteLine($"[{nameof(OpenXrInitializer)}] XR_EXT_hand_tracking is not supported by runtime: {runtimeName}");
        }
        if (!hasHandInteraction)
        {
            Trace.WriteLine($"[{nameof(OpenXrInitializer)}] XR_EXT_hand_interaction is not supported by runtime: {runtimeName}");
        }

        return true;
    }

    public static bool TryGetSystem(
        Silk.NET.OpenXR.XR xr,
        Instance instance,
        out ulong systemId,
        out string systemName)
    {
        systemId = 0;
        systemName = "Unknown";

        var systemGetInfo = new SystemGetInfo
        {
            Type = StructureType.SystemGetInfo,
            Next = null,
            FormFactor = FormFactor.HeadMountedDisplay
        };

        ulong sid;
        var sysRes = xr.GetSystem(instance, &systemGetInfo, &sid);
        if (sysRes == Result.ErrorFormFactorUnavailable)
        {
            Trace.TraceError($"[{nameof(OpenXrInitializer)}] The headset is likely not connected!");
            return false;
        }
        else if (sysRes != Result.Success)
        {
            Trace.TraceWarning($"[{nameof(OpenXrInitializer)}] xrGetSystem failed: {sysRes}");
            return false;
        }
        systemId = sid;

        var systemProps = new SystemProperties
        {
            Type = StructureType.SystemProperties,
            Next = null
        };
        xr.GetSystemProperties(instance, sid, &systemProps);
        systemName = Marshal.PtrToStringAnsi((IntPtr)systemProps.SystemName) ?? "Unknown";

        Trace.TraceInformation($"[{nameof(OpenXrInitializer)}] System acquired: {systemId} ({systemName})");
        return true;
    }
}