using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;
using System;
using System.Diagnostics;
using System.Text;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;

namespace OpenXrUsage.Core.Infrastructure;

public static unsafe class GraphicsBackendInitializer
{
    public static bool IsInitialized { get; private set; }
    internal static string OpenXrRuntimeName;
    internal static string OpenXrSystemName;
    private static bool _didFinish;

    public static bool TryConfigureGraphicsBackendExtensions(out bool isSuccess)
    {
        if (_didFinish)
        {
            isSuccess = IsInitialized;
            return _didFinish;
        }
        _didFinish = true;

        var graphicsBackend = PlatformInfo.GraphicsBackend;

        // DirectX 12 does not require passing extension strings to the graphics device.
        if (graphicsBackend == GraphicsBackend.DirectX12)
        {
            IsInitialized = true;
            isSuccess = true;

            return _didFinish;
        }
        else if (graphicsBackend != GraphicsBackend.Vulkan)
        {
            // If ever there are other graphics backends, we may need to query OpenXR for other types of extensions, and handle them for those backends.
            Trace.TraceWarning($"[{nameof(GraphicsBackendInitializer)}] Unsupported graphics backend: {graphicsBackend}");
            IsInitialized = false;
            isSuccess = false;

            return _didFinish;
        }

        string instanceExtensions = string.Empty;
        string deviceExtensions = string.Empty;

        Silk.NET.OpenXR.XR xr = default;
        Instance instance = default;

        try
        {
            xr = Silk.NET.OpenXR.XR.GetApi();
            instance = default;

            // Create a minimal OpenXR instance so we can query the required Vulkan extension strings.
            if (!OpenXrInitializer.TryCreateInstance(
                    xr,
                    graphicsBackend,
                    out instance,
                    out OpenXrRuntimeName,
                    out _,
                    out _))
            {
                IsInitialized = false;
                isSuccess = IsInitialized;
                return _didFinish;
            }

            if (!OpenXrInitializer.TryGetSystem(
                    xr,
                    instance,
                    out ulong systemId,
                    out OpenXrSystemName))
            {
                IsInitialized = false;
                isSuccess = IsInitialized;
                return _didFinish;
            }

            if (xr.TryGetInstanceExtension(null, instance, out KhrVulkanEnable vulkanEnable))
            {
                uint instSize = 0;
                vulkanEnable.GetVulkanInstanceExtension(instance, systemId, 0, &instSize, (byte*)null);
                if (instSize > 0)
                {
                    byte[] buffer = new byte[instSize];
                    fixed (byte* pBuf = buffer)
                    {
                        var res = vulkanEnable.GetVulkanInstanceExtension(instance, systemId, instSize, &instSize, pBuf);
                        if (res == Result.Success)
                        {
                            instanceExtensions = Encoding.UTF8.GetString(buffer).TrimEnd('\0').Trim();
                            Trace.TraceInformation($"[{nameof(GraphicsBackendInitializer)}] Required Vulkan instance extensions: {instanceExtensions}");
                            if (!string.IsNullOrWhiteSpace(instanceExtensions))
                            {
                                // This is where we use the proposed SetRequiredExtensions method
                                // to enable Vulkan (or any other future backend) extensions that OpenXR requires.
                                GraphicsDevice.SetRequiredExtensions(instanceExtensions, null);
                            }
                        }
                    }
                }

                uint devSize = 0;
                vulkanEnable.GetVulkanDeviceExtension(instance, systemId, 0, &devSize, (byte*)null);
                if (devSize > 0)
                {
                    byte[] buffer = new byte[devSize];
                    fixed (byte* pBuf = buffer)
                    {
                        var res = vulkanEnable.GetVulkanDeviceExtension(instance, systemId, devSize, &devSize, pBuf);
                        if (res == Result.Success)
                        {
                            deviceExtensions = Encoding.UTF8.GetString(buffer).TrimEnd('\0').Trim();
                            Debug.WriteLine($"[{nameof(GraphicsBackendInitializer)}] Required Vulkan device extensions: {deviceExtensions}");
                            if (!string.IsNullOrWhiteSpace(deviceExtensions))
                            {
                                // Just copied over my old code. Can't be bothered to merge them.
                                GraphicsDevice.SetRequiredExtensions(null, deviceExtensions);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[{nameof(GraphicsBackendInitializer)}] Failed to configure backend extensions: [{nameof(instanceExtensions)}: {instanceExtensions}] [{nameof(deviceExtensions)}: {deviceExtensions}] Exception: {ex}");
            IsInitialized = false;
            isSuccess = IsInitialized;
            return _didFinish;
        }
        finally
        {
            if (instance.Handle != 0)
            {
                xr?.DestroyInstance(instance);
            }
            xr?.Dispose();
        }

        IsInitialized = true;
        isSuccess = IsInitialized;
        return _didFinish;
    }
}