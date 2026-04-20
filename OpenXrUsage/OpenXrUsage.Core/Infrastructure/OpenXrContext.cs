using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.EXT;
using Silk.NET.OpenXR.Extensions.KHR;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace OpenXrUsage.Core.Infrastructure;

public sealed unsafe class OpenXrContext : IDisposable
{
    public bool HasHandTracking => _hasHandTracking;
    public bool HasHandInteraction => _hasHandInteraction;
    internal static string OpenXrRuntimeName;
    internal static string OpenXrSystemName;
    private Silk.NET.OpenXR.XR _xr;
    private Instance _instance;
    private ulong _systemId;
    private Session _session;
    private Space _appSpace;

    private Swapchain[] _swapchains = Array.Empty<Swapchain>();
    private nint[][] _swapchainImageHandles = Array.Empty<nint[]>();
    private int[] _currentSwapchainImageIndex = Array.Empty<int>();

    private ViewConfigurationView[] _viewConfigViews = Array.Empty<ViewConfigurationView>();
    private View[] _views = Array.Empty<View>();
    private FrameState _frameState;

    private SessionState _sessionState = SessionState.Unknown;
    private bool _sessionRunning;
    private bool _disposed;

    private KhrVulkanEnable? _vulkanEnable;
    private KhrD3D12Enable? _d3d12Enable;
    private ExtHandTracking? _handTracking;
    private GraphicsBackend _graphicsBackend;

    private bool _hasHandTracking;
    private bool _hasHandInteraction;
    private HandTrackerEXT? _leftHandTracker;
    private HandTrackerEXT? _rightHandTracker;


    private ActionSet _actionSet;
    private Silk.NET.OpenXR.Action _pinchAction;
    private Silk.NET.OpenXR.Action _grabAction;
    private Silk.NET.OpenXR.Action _grabReadyAction;
    private Silk.NET.OpenXR.Action _poseAction;
    private Silk.NET.OpenXR.Action _gripPoseAction;
    private bool _leftPinchActive;
    private bool _rightPinchActive;
    private bool _leftGrabActive;
    private bool _rightGrabActive;
    private Space _leftPoseSpace;
    private Space _rightPoseSpace;
    private Space _leftGripSpace;
    private Space _rightGripSpace;

    public Space LeftPoseSpace => _leftPoseSpace;
    public Space RightPoseSpace => _rightPoseSpace;
    public Space LeftGripSpace => _leftGripSpace;
    public Space RightGripSpace => _rightGripSpace;

    private ulong _leftHandPath;
    private ulong _rightHandPath;
    private static readonly TimeSpan ErrorSyncPeriod = TimeSpan.FromSeconds(5);
    private DateTime _lastErrorSyncTimestamp;

    /// <summary>Whether the OpenXR session is currently running and rendering.</summary>
    public bool IsSessionRunning => _sessionRunning;

    /// <summary>Whether the runtime says we should render this frame.</summary>
    public bool ShouldRender => _frameState.ShouldRender != 0;

    /// <summary>The current per-eye views (pose + FOV). Updated by <see cref="LocateViews"/>.</summary>
    public ReadOnlySpan<View> Views => _views;

    /// <summary>The runtime-recommended render resolution per eye.</summary>
    public ReadOnlySpan<ViewConfigurationView> ViewConfigViews => _viewConfigViews;

    /// <summary>Number of views (eyes). Typically 2 for stereo.</summary>
    public int ViewCount => _views.Length;

    /// <summary>The current frame's predicted display time.</summary>
    public long PredictedDisplayTime => _frameState.PredictedDisplayTime;

    /// <summary>The Session handle.</summary>
    public Session Session => _session;

    public float LeftPinchValue => GetActionStateFloat(_pinchAction, _leftHandPath);
    public float RightPinchValue => GetActionStateFloat(_pinchAction, _rightHandPath);
    public float LeftGrabValue => GetActionStateFloat(_grabAction, _leftHandPath);
    public float RightGrabValue => GetActionStateFloat(_grabAction, _rightHandPath);
    public bool LeftGrabReady => GetActionStateBool(_grabReadyAction, _leftHandPath);
    public bool RightGrabReady => GetActionStateBool(_grabReadyAction, _rightHandPath);

    /// <summary>The Space used for locating views.</summary>
    public Space AppSpace => _appSpace;

    /// <summary>Per-eye swapchain handles.</summary>
    public ReadOnlySpan<Swapchain> Swapchains => _swapchains;

    public OpenXrContext()
    {
        _xr = Silk.NET.OpenXR.XR.GetApi();
    }

    /// <summary>
    /// Initializes the OpenXR runtime, system, session, reference space, and swapchains.
    /// </summary>
    /// <returns>True if initialization succeeded.</returns>
    public bool Initialize(NativeGraphicsHandles graphicsHandles)
    {
        _graphicsBackend = graphicsHandles.Backend;

        if (!CreateInstance())
        {
            return false;
        }
        if (!GetSystem())
        {
            return false;
        }
        if (!CheckRequirements())
        {
            return false;
        }
        if (!CreateSession(graphicsHandles))
        {
            return false;
        }
        if (!CreateReferenceSpace())
        {
            return false;
        }
        if (!EnumerateViewConfigurations())
        {
            return false;
        }
        if (!CreateSwapchains())
        {
            return false;
        }

        if (!SetupInteraction())
        {
            // No need to return false if interaction setup fails, as it's optional. Just log it.
            Trace.TraceWarning($"[{nameof(OpenXrContext)}] Hand tracking and/or hand interaction is not available.");
        }

        Debug.WriteLine($"[{nameof(OpenXrContext)}] Initialization complete.");
        return true;
    }

    #region Instance / System

    private bool CreateInstance()
    {
        if (!OpenXrInitializer.TryCreateInstance(
                _xr,
                _graphicsBackend,
                out _instance,
                out OpenXrRuntimeName,
                out bool hasHandTracking,
                out bool hasHandInteraction))
        {
            return false;
        }

        _hasHandTracking = hasHandTracking;
        _hasHandInteraction = hasHandInteraction;

        // Load the appropriate graphics binding extension
        switch (_graphicsBackend)
        {
            case GraphicsBackend.DirectX12:
                {
                    if (!_xr.TryGetInstanceExtension(null, _instance, out _d3d12Enable))
                    {
                        Trace.WriteLine($"[{nameof(OpenXrContext)}] Failed to load XR_KHR_D3D12_enable extension.");
                        try
                        {
                            _d3d12Enable = new KhrD3D12Enable(_xr.Context);
                            Trace.WriteLine($"[{nameof(OpenXrContext)}] Manually instantiated KhrD3D12Enable.");
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    break;
                }
            case GraphicsBackend.Vulkan:
                {
                    if (!_xr.TryGetInstanceExtension(null, _instance, out _vulkanEnable))
                    {
                        Trace.WriteLine($"[{nameof(OpenXrContext)}] Failed to load XR_KHR_vulkan_enable extension.");
                        try
                        {
                            _vulkanEnable = new KhrVulkanEnable(_xr.Context);
                            Trace.WriteLine($"[{nameof(OpenXrContext)}] Manually instantiated KhrVulkanEnable.");
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    break;
                }
            default:
                {
                    throw new InvalidOperationException($"Unsupported graphics backend: {_graphicsBackend}");
                }
        }

        // Load Hand Tracking extension
        if (_xr.TryGetInstanceExtension(null, _instance, out _handTracking))
        {
            Trace.WriteLine($"[{nameof(OpenXrContext)}] XR_EXT_hand_tracking extension loaded.");
        }

        return true;
    }

    private bool GetSystem()
    {
        if (!OpenXrInitializer.TryGetSystem(
                _xr,
                _instance,
                out _systemId,
                out OpenXrSystemName))
        {
            return false;
        }

        return true;
    }

    #endregion

    #region Session

    private bool CheckRequirements()
    {
        switch (_graphicsBackend)
        {
            case GraphicsBackend.DirectX12:
            {
                if (_d3d12Enable == null)
                {
                    return false;
                }

                var graphicsRequirements = new GraphicsRequirementsD3D12KHR
                {
                    Type = StructureType.GraphicsRequirementsD3D12Khr,
                    Next = null
                };

                var reqResult = _d3d12Enable.GetD3D12GraphicsRequirements(_instance, _systemId, &graphicsRequirements);
                if (reqResult != Result.Success)
                {
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] GetD3D12GraphicsRequirements failed: {reqResult}");
                    return false;
                }

                Debug.WriteLine($"[{nameof(OpenXrContext)}] D3D12 graphics requirements: " +
                                $"minFeatureLevel={graphicsRequirements.MinFeatureLevel}");
                return true;
            }
            case GraphicsBackend.Vulkan:
            {
                if (_vulkanEnable == null)
                {
                    return false;
                }

                var graphicsRequirements = new GraphicsRequirementsVulkanKHR
                {
                    Type = StructureType.GraphicsRequirementsVulkanKhr,
                    Next = null
                };

                var reqResult = _vulkanEnable.GetVulkanGraphicsRequirements(_instance, _systemId, &graphicsRequirements);
                if (reqResult != Result.Success)
                {
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] GetVulkanGraphicsRequirements failed: {reqResult}");
                    return false;
                }

                Debug.WriteLine($"[{nameof(OpenXrContext)}] Vulkan graphics requirements: " +
                                $"minApiVersion={graphicsRequirements.MinApiVersionSupported}, " +
                                $"maxApiVersion={graphicsRequirements.MaxApiVersionSupported}");
                return true;
            }
            default:
            {
                throw new InvalidOperationException($"Unsupported graphics backend: {_graphicsBackend}");
            }
        }
    }

    private bool CreateSession(NativeGraphicsHandles handles)
    {
        switch (_graphicsBackend)
        {
            case GraphicsBackend.DirectX12:
            {
                return CreateDx12Session(handles);
            }
            case GraphicsBackend.Vulkan:
            {
                return CreateVulkanSession(handles);
            }
            default:
            {
                throw new InvalidOperationException($"Unsupported graphics backend: {_graphicsBackend}");
            }
        }
    }

    private bool CreateVulkanSession(NativeGraphicsHandles handles)
    {
        var binding = new GraphicsBindingVulkanKHR
        {
            Type = StructureType.GraphicsBindingVulkanKhr,
            Next = null,
            Instance = new VkHandle(handles.Instance),
            PhysicalDevice = new VkHandle(handles.PhysicalDevice),
            Device = new VkHandle(handles.LogicalDevice),
            QueueFamilyIndex = (uint)handles.QueueFamilyIndex,
            QueueIndex = (uint)handles.QueueIndex,
        };

        return FinalizeSession(&binding);
    }

    private bool CreateDx12Session(NativeGraphicsHandles handles)
    {
        var binding = new GraphicsBindingD3D12KHR
        {
            Type = StructureType.GraphicsBindingD3D12Khr,
            Next = null,
            Device = (void*)handles.LogicalDevice,
            Queue = (void*)handles.Queue,
        };

        return FinalizeSession(&binding);
    }

    private bool FinalizeSession(void* bindingPtr)
    {
        var sessionCreateInfo = new SessionCreateInfo
        {
            Type = StructureType.SessionCreateInfo,
            Next = bindingPtr,
            CreateFlags = 0,
            SystemId = _systemId,
        };

        Session session;
        var result = _xr.CreateSession(_instance, &sessionCreateInfo, &session);
        if (result == Result.ErrorGraphicsDeviceInvalid)
        {
            Trace.TraceError("Invalid graphics device.");
            return false;
        }
        if (result != Result.Success)
        {
            Debug.WriteLine($"[{nameof(OpenXrContext)}] xrCreateSession failed: {result}");
            return false;
        }
        _session = session;

        Debug.WriteLine($"[{nameof(OpenXrContext)}] Session created ({_graphicsBackend}).");
        return true;
    }

    private bool CreateReferenceSpace()
    {
        var identityPose = new Posef
        {
            Orientation = new Quaternionf { X = 0, Y = 0, Z = 0, W = 1 },
            Position = new Vector3f { X = 0, Y = 0, Z = 0 },
        };

        var spaceCreateInfo = new ReferenceSpaceCreateInfo
        {
            Type = StructureType.ReferenceSpaceCreateInfo,
            Next = null,
            ReferenceSpaceType = ReferenceSpaceType.Local,
            PoseInReferenceSpace = identityPose,
        };

        Space space;
        var result = _xr.CreateReferenceSpace(_session, &spaceCreateInfo, &space);
        if (result != Result.Success)
        {
            Debug.WriteLine($"[{nameof(OpenXrContext)}] xrCreateReferenceSpace failed: {result}");
            return false;
        }
        _appSpace = space;

        Debug.WriteLine($"[{nameof(OpenXrContext)}] Reference space (LOCAL) created.");
        return true;
    }

    #endregion

    #region View Configuration

    private bool EnumerateViewConfigurations()
    {
        uint viewCount = 0;
        var result = _xr.EnumerateViewConfigurationView(
            _instance,
            _systemId,
            ViewConfigurationType.PrimaryStereo,
            0,
            &viewCount,
            null);

        if (result != Result.Success || viewCount == 0)
        {
            Debug.WriteLine($"[{nameof(OpenXrContext)}] EnumerateViewConfigurationView (count) failed: {result}");
            return false;
        }

        _viewConfigViews = new ViewConfigurationView[viewCount];
        for (int i = 0; i < viewCount; i++)
        {
            _viewConfigViews[i].Type = StructureType.ViewConfigurationView;
            _viewConfigViews[i].Next = null;
        }

        fixed (ViewConfigurationView* ptr = _viewConfigViews)
        {
            result = _xr.EnumerateViewConfigurationView(
                _instance,
                _systemId,
                ViewConfigurationType.PrimaryStereo,
                viewCount,
                &viewCount,
                ptr);
        }

        if (result != Result.Success)
        {
            Debug.WriteLine($"[{nameof(OpenXrContext)}] EnumerateViewConfigurationView (data) failed: {result}");
            return false;
        }

        _views = new View[viewCount];
        for (int i = 0; i < viewCount; i++)
        {
            _views[i].Type = StructureType.View;
            _views[i].Next = null;
        }

        Debug.WriteLine($"[{nameof(OpenXrContext)}] {viewCount} views configured. " +
                        $"Recommended resolution: {_viewConfigViews[0].RecommendedImageRectWidth}x{_viewConfigViews[0].RecommendedImageRectHeight}");
        return true;
    }

    #endregion

    #region Swapchains

    private bool CreateSwapchains()
    {
        int eyeCount = _viewConfigViews.Length;
        _swapchains = new Swapchain[eyeCount];
        _swapchainImageHandles = new nint[eyeCount][];
        _currentSwapchainImageIndex = new int[eyeCount];

        // Select the correct swapchain format for the active backend.
        // VK_FORMAT_R8G8B8A8_UNORM = 37, DXGI_FORMAT_R8G8B8A8_UNORM = 28
        long swapchainFormat = _graphicsBackend switch
        {
            GraphicsBackend.DirectX12 => 28,
            _ => 37,
        };

        for (int eye = 0; eye < eyeCount; eye++)
        {
            ref var viewConfig = ref _viewConfigViews[eye];

            var swapchainCreateInfo = new SwapchainCreateInfo
            {
                Type = StructureType.SwapchainCreateInfo,
                Next = null,
                CreateFlags = 0,
                UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
                Format = swapchainFormat,
                SampleCount = viewConfig.RecommendedSwapchainSampleCount,
                Width = viewConfig.RecommendedImageRectWidth,
                Height = viewConfig.RecommendedImageRectHeight,
                FaceCount = 1,
                ArraySize = 1,
                MipCount = 1
            };

            Swapchain swapchain;
            var result = _xr.CreateSwapchain(_session, &swapchainCreateInfo, &swapchain);
            if (result != Result.Success)
            {
                Debug.WriteLine($"[{nameof(OpenXrContext)}] xrCreateSwapchain failed for eye {eye}: {result}");
                return false;
            }
            _swapchains[eye] = swapchain;

            // Enumerate and extract native image handles. (Backend-specific.)
            if (!EnumerateSwapchainImages(swapchain, eye))
            {
                return false;
            }

            Debug.WriteLine($"[{nameof(OpenXrContext)}] Eye {eye}: swapchain created with {_swapchainImageHandles[eye].Length} images ({_graphicsBackend}).");
        }

        return true;
    }

    /// <summary>
    /// Enumerates swapc-hain images using the correct backend-specific Silk.NET type,
    /// then extracts the native image handles into a flat <c>nint[]</c>.
    /// </summary>
    private bool EnumerateSwapchainImages(Swapchain swapchain, int eyeIndex)
    {
        uint imageCount = 0;
        var result = _xr.EnumerateSwapchainImages(swapchain, 0, &imageCount, null);
        if (result != Result.Success || imageCount == 0)
        {
            Debug.WriteLine($"[{nameof(OpenXrContext)}] EnumerateSwapchainImages (count) failed for eye {eyeIndex}: {result}");
            return false;
        }

        switch (_graphicsBackend)
        {
            case GraphicsBackend.DirectX12:
            {
                var images = new SwapchainImageD3D12KHR[imageCount];
                for (int i = 0; i < imageCount; i++)
                {
                    images[i].Type = StructureType.SwapchainImageD3D12Khr;
                    images[i].Next = null;
                }

                fixed (SwapchainImageD3D12KHR* imagesPtr = images)
                {
                    result = _xr.EnumerateSwapchainImages(
                        swapchain, imageCount, &imageCount,
                        (SwapchainImageBaseHeader*)imagesPtr);
                }

                if (result != Result.Success)
                {
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] EnumerateSwapchainImages (data) failed for eye {eyeIndex}: {result}");
                    return false;
                }

                _swapchainImageHandles[eyeIndex] = new nint[imageCount];
                for (int i = 0; i < imageCount; i++)
                {
                    _swapchainImageHandles[eyeIndex][i] = (nint)images[i].Texture;
                }
                break;
            }
            default: // Vulkan
            {
                var images = new SwapchainImageVulkanKHR[imageCount];
                for (int i = 0; i < imageCount; i++)
                {
                    images[i].Type = StructureType.SwapchainImageVulkanKhr;
                    images[i].Next = null;
                }

                fixed (SwapchainImageVulkanKHR* imagesPtr = images)
                {
                    result = _xr.EnumerateSwapchainImages(
                        swapchain, imageCount, &imageCount,
                        (SwapchainImageBaseHeader*)imagesPtr);
                }

                if (result != Result.Success)
                {
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] EnumerateSwapchainImages (data) failed for eye {eyeIndex}: {result}");
                    return false;
                }

                _swapchainImageHandles[eyeIndex] = new nint[imageCount];
                for (int i = 0; i < imageCount; i++)
                {
                    _swapchainImageHandles[eyeIndex][i] = (nint)images[i].Image;
                }
                break;
            }
        }

        return true;
    }

    private bool SetupInteraction()
    {
        if (_hasHandTracking && _handTracking != null)
        {
            var leftHandCreateInfo = new HandTrackerCreateInfoEXT
            {
                Type = StructureType.HandTrackerCreateInfoExt,
                Next = null,
                Hand = HandEXT.LeftExt,
                HandJointSet = HandJointSetEXT.DefaultExt,
            };

            var rightHandCreateInfo = new HandTrackerCreateInfoEXT
            {
                Type = StructureType.HandTrackerCreateInfoExt,
                Next = null,
                Hand = HandEXT.RightExt,
                HandJointSet = HandJointSetEXT.DefaultExt,
            };

            HandTrackerEXT leftHandTracker;
            var resLeft = _handTracking.CreateHandTracker(_session, &leftHandCreateInfo, &leftHandTracker);
            if (resLeft != Result.Success)
            {
                Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create left hand tracker: {resLeft}");
            }
            else
            {
                _leftHandTracker = leftHandTracker;
                Trace.TraceInformation($"[{nameof(OpenXrContext)}] Left hand tracker created.");
            }

            HandTrackerEXT rightHandTracker;
            var resRight = _handTracking.CreateHandTracker(_session, &rightHandCreateInfo, &rightHandTracker);
            if (resRight != Result.Success)
            {
                Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create right hand tracker: {resRight}");
            }
            else
            {
                _rightHandTracker = rightHandTracker;
                Trace.TraceInformation($"[{nameof(OpenXrContext)}] Right hand tracker created.");
            }
        }

        // 1. Create Action Set
        var actionSetInfo = new ActionSetCreateInfo
        {
            Type = StructureType.ActionSetCreateInfo,
            Next = null,
            Priority = 0
        };

        fixed (byte* name = "main_actions\0"u8.ToArray())
        fixed (byte* locName = "Main Actions\0"u8.ToArray())
        {
            for (int i = 0; i < "main_actions".Length; i++)
            {
                actionSetInfo.ActionSetName[i] = name[i];
            }
            for (int i = 0; i < "Main Actions".Length; i++)
            {
                actionSetInfo.LocalizedActionSetName[i] = locName[i];
            }
        }

        var resActionSet = _xr.CreateActionSet(_instance, &actionSetInfo, ref _actionSet);
        if (resActionSet != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create action set: {resActionSet}");
            return false;
        }

        // Setup action paths.
        ulong leftPath = 0, rightPath = 0;
        fixed (byte* pPathL = "/user/hand/left\0"u8.ToArray())
            _xr.StringToPath(_instance, pPathL, ref leftPath);
        fixed (byte* pPathR = "/user/hand/right\0"u8.ToArray())
            _xr.StringToPath(_instance, pPathR, ref rightPath);

        _leftHandPath = leftPath;
        _rightHandPath = rightPath;

        ulong* subactionPaths = stackalloc ulong[] { leftPath, rightPath };

        // Create Pinch Action.
        var pinchActionInfo = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo,
            Next = null,
            ActionType = ActionType.FloatInput,
            CountSubactionPaths = 2,
            SubactionPaths = subactionPaths
        };

        fixed (byte* name = "pinch_value\0"u8.ToArray())
        fixed (byte* locName = "Pinch Value\0"u8.ToArray())
        {
            for (int i = 0; i < "pinch_value".Length; i++)
            {
                pinchActionInfo.ActionName[i] = name[i];
            }
            for (int i = 0; i < "Pinch Value".Length; i++)
            {
                pinchActionInfo.LocalizedActionName[i] = locName[i];
            }
        }

        Silk.NET.OpenXR.Action pinchAction = default;
        var resPinch = _xr.CreateAction(_actionSet, &pinchActionInfo, ref pinchAction);
        if (resPinch != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create pinch action: {resPinch}");
        }
        else
        {
            _pinchAction = pinchAction;
        }

        // Create Grab Action.
        var grabActionInfo = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo,
            Next = null,
            ActionType = ActionType.FloatInput,
            CountSubactionPaths = 2,
            SubactionPaths = subactionPaths
        };

        fixed (byte* name = "grasp_value\0"u8.ToArray())
        fixed (byte* locName = "Grasp Value\0"u8.ToArray())
        {
            for (int i = 0; i < "grasp_value".Length; i++)
            {
                grabActionInfo.ActionName[i] = name[i];
            }
            for (int i = 0; i < "Grasp Value".Length; i++)
            {
                grabActionInfo.LocalizedActionName[i] = locName[i];
            }
        }

        Silk.NET.OpenXR.Action grabAction = default;
        var resGrab = _xr.CreateAction(_actionSet, &grabActionInfo, ref grabAction);
        if (resGrab != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create grab action: {resGrab}");
        }
        else
        {
            _grabAction = grabAction;
        }

        // Create Grab Ready Action.
        var grabReadyActionInfo = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo,
            Next = null,
            ActionType = ActionType.BooleanInput,
            CountSubactionPaths = 2,
            SubactionPaths = subactionPaths
        };

        fixed (byte* name = "grasp_ready\0"u8.ToArray())
        fixed (byte* locName = "Grasp Ready\0"u8.ToArray())
        {
            for (int i = 0; i < "grasp_ready".Length; i++)
            {
                grabReadyActionInfo.ActionName[i] = name[i];
            }
            for (int i = 0; i < "Grasp Ready".Length; i++)
            {
                grabReadyActionInfo.LocalizedActionName[i] = locName[i];
            }
        }

        Silk.NET.OpenXR.Action grabReadyAction = default;
        var resGrabReady = _xr.CreateAction(_actionSet, &grabReadyActionInfo, ref grabReadyAction);
        if (resGrabReady != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create grab ready action: {resGrabReady}");
        }
        else
        {
            _grabReadyAction = grabReadyAction;
        }

        // Create Pose Action.
        var poseActionInfo = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo,
            Next = null,
            ActionType = ActionType.PoseInput,
            CountSubactionPaths = 2,
            SubactionPaths = subactionPaths
        };

        fixed (byte* name = "aim_pose\0"u8.ToArray())
        fixed (byte* locName = "Aim Pose\0"u8.ToArray())
        {
            for (int i = 0; i < "aim_pose".Length; i++)
            {
                poseActionInfo.ActionName[i] = name[i];
            }
            for (int i = 0; i < "Aim Pose".Length; i++)
            {
                poseActionInfo.LocalizedActionName[i] = locName[i];
            }
        }

        Silk.NET.OpenXR.Action poseAction = default;
        var resPose = _xr.CreateAction(_actionSet, &poseActionInfo, ref poseAction);
        if (resPose != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create pose action: {resPose}");
        }
        else
        {
            _poseAction = poseAction;
        }

        // Create Grip Pose Action. (Tracks physical hand position in all 3 axes.)
        var gripPoseActionInfo = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo,
            Next = null,
            ActionType = ActionType.PoseInput,
            CountSubactionPaths = 2,
            SubactionPaths = subactionPaths
        };

        fixed (byte* name = "grip_pose\0"u8.ToArray())
        fixed (byte* locName = "Grip Pose\0"u8.ToArray())
        {
            for (int i = 0; i < "grip_pose".Length; i++)
            {
                gripPoseActionInfo.ActionName[i] = name[i];
            }
            for (int i = 0; i < "Grip Pose".Length; i++)
            {
                gripPoseActionInfo.LocalizedActionName[i] = locName[i];
            }
        }

        Silk.NET.OpenXR.Action gripPoseAction = default;
        var resGripPose = _xr.CreateAction(_actionSet, &gripPoseActionInfo, ref gripPoseAction);
        if (resGripPose != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to create grip pose action: {resGripPose}");
        }
        else
        {
            _gripPoseAction = gripPoseAction;
        }

        ulong leftAimPath = 0, rightAimPath = 0;
        fixed (byte* pPathL = "/user/hand/left/input/aim/pose\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPathL, ref leftAimPath);
        }
        fixed (byte* pPathR = "/user/hand/right/input/aim/pose\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPathR, ref rightAimPath);
        }

        ulong leftGripPosePath = 0, rightGripPosePath = 0;
        fixed (byte* pPathL = "/user/hand/left/input/grip/pose\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPathL, ref leftGripPosePath);
        }
        fixed (byte* pPathR = "/user/hand/right/input/grip/pose\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPathR, ref rightGripPosePath);
        }

        if (_hasHandInteraction)
        {
            // Suggest Bindings for XR_EXT_hand_interaction.
            ulong handInteractionProfilePath = 0;
            fixed (byte* pPath = "/interaction_profiles/ext/hand_interaction_ext\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPath, ref handInteractionProfilePath);
            }

            ulong leftPinchPath = 0, rightPinchPath = 0;
            fixed (byte* pPathL = "/user/hand/left/input/pinch_ext/value\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathL, ref leftPinchPath);
            }
            fixed (byte* pPathR = "/user/hand/right/input/pinch_ext/value\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathR, ref rightPinchPath);
            }

            ulong leftGraspPath = 0, rightGraspPath = 0;
            fixed (byte* pPathL = "/user/hand/left/input/grasp_ext/value\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathL, ref leftGraspPath);
            }
            fixed (byte* pPathR = "/user/hand/right/input/grasp_ext/value\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathR, ref rightGraspPath);
            }

            ulong leftGraspReadyPath = 0, rightGraspReadyPath = 0;
            fixed (byte* pPathL = "/user/hand/left/input/grasp_ext/ready_ext\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathL, ref leftGraspReadyPath);
            }
            fixed (byte* pPathR = "/user/hand/right/input/grasp_ext/ready_ext\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathR, ref rightGraspReadyPath);
            }

            var bindings = stackalloc ActionSuggestedBinding[10];
            bindings[0] = new ActionSuggestedBinding { Action = _pinchAction, Binding = leftPinchPath };
            bindings[1] = new ActionSuggestedBinding { Action = _pinchAction, Binding = rightPinchPath };
            bindings[2] = new ActionSuggestedBinding { Action = _grabAction, Binding = leftGraspPath };
            bindings[3] = new ActionSuggestedBinding { Action = _grabAction, Binding = rightGraspPath };
            bindings[4] = new ActionSuggestedBinding { Action = _grabReadyAction, Binding = leftGraspReadyPath };
            bindings[5] = new ActionSuggestedBinding { Action = _grabReadyAction, Binding = rightGraspReadyPath };
            bindings[6] = new ActionSuggestedBinding { Action = _poseAction, Binding = leftAimPath };
            bindings[7] = new ActionSuggestedBinding { Action = _poseAction, Binding = rightAimPath };
            bindings[8] = new ActionSuggestedBinding { Action = _gripPoseAction, Binding = leftGripPosePath };
            bindings[9] = new ActionSuggestedBinding { Action = _gripPoseAction, Binding = rightGripPosePath };

            var suggestedBindings = new InteractionProfileSuggestedBinding
            {
                Type = StructureType.InteractionProfileSuggestedBinding,
                Next = null,
                InteractionProfile = handInteractionProfilePath,
                CountSuggestedBindings = 10,
                SuggestedBindings = bindings
            };

            var resBindings = _xr.SuggestInteractionProfileBinding(_instance, &suggestedBindings);
            if (resBindings != Result.Success)
            {
                Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to suggest hand interaction bindings: {resBindings}");
            }
            else
            {
                Trace.TraceInformation($"[{nameof(OpenXrContext)}] Hand interaction bindings suggested successfully.");
            }
        }
        else
        {
            Trace.TraceWarning($"[{nameof(OpenXrContext)}] XR_EXT_hand_interaction not supported/enabled. Native pinch actions will likely be inactive.");
        }

        // Suggest Bindings for KHR Simple Controller. (Fallback)
        {
            ulong simpleProfilePath = 0;
            fixed (byte* pPath = "/interaction_profiles/khr/simple_controller\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPath, ref simpleProfilePath);
            }

            ulong leftSelectPath = 0, rightSelectPath = 0;
            fixed (byte* pPathL = "/user/hand/left/input/select/click\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathL, ref leftSelectPath);
            }
            fixed (byte* pPathR = "/user/hand/right/input/select/click\0"u8.ToArray())
            {
                _xr.StringToPath(_instance, pPathR, ref rightSelectPath);
            }

            var simpleBindings = stackalloc ActionSuggestedBinding[6];
            simpleBindings[0] = new ActionSuggestedBinding { Action = _pinchAction, Binding = leftSelectPath };
            simpleBindings[1] = new ActionSuggestedBinding { Action = _pinchAction, Binding = rightSelectPath };
            simpleBindings[2] = new ActionSuggestedBinding { Action = _poseAction, Binding = leftAimPath };
            simpleBindings[3] = new ActionSuggestedBinding { Action = _poseAction, Binding = rightAimPath };
            simpleBindings[4] = new ActionSuggestedBinding { Action = _gripPoseAction, Binding = leftGripPosePath };
            simpleBindings[5] = new ActionSuggestedBinding { Action = _gripPoseAction, Binding = rightGripPosePath };

            var simpleSuggestedBindings = new InteractionProfileSuggestedBinding
            {
                Type = StructureType.InteractionProfileSuggestedBinding,
                Next = null,
                InteractionProfile = simpleProfilePath,
                CountSuggestedBindings = 6,
                SuggestedBindings = simpleBindings
            };

            var resSimple = _xr.SuggestInteractionProfileBinding(_instance, &simpleSuggestedBindings);
            if (resSimple != Result.Success) Trace.TraceWarning($"[{nameof(OpenXrContext)}] Failed to suggest simple bindings: {resSimple}");
        }

        // Suggest Bindings for Oculus Touch Controller. (Native grip poses.)
        ulong oculusProfilePath = 0;
        fixed (byte* pPath = "/interaction_profiles/oculus/touch_controller\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPath, ref oculusProfilePath);
        }

        ulong leftTriggerPath = 0, rightTriggerPath = 0;
        fixed (byte* pPathL = "/user/hand/left/input/trigger/value\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPathL, ref leftTriggerPath);
        }
        fixed (byte* pPathR = "/user/hand/right/input/trigger/value\0"u8.ToArray())
        {
            _xr.StringToPath(_instance, pPathR, ref rightTriggerPath);
        }

        var oculusBindings = stackalloc ActionSuggestedBinding[6];
        oculusBindings[0] = new ActionSuggestedBinding { Action = _pinchAction, Binding = leftTriggerPath };
        oculusBindings[1] = new ActionSuggestedBinding { Action = _pinchAction, Binding = rightTriggerPath };
        oculusBindings[2] = new ActionSuggestedBinding { Action = _poseAction, Binding = leftAimPath };
        oculusBindings[3] = new ActionSuggestedBinding { Action = _poseAction, Binding = rightAimPath };
        oculusBindings[4] = new ActionSuggestedBinding { Action = _gripPoseAction, Binding = leftGripPosePath };
        oculusBindings[5] = new ActionSuggestedBinding { Action = _gripPoseAction, Binding = rightGripPosePath };

        var oculusSuggestedBindings = new InteractionProfileSuggestedBinding
        {
            Type = StructureType.InteractionProfileSuggestedBinding,
            Next = null,
            InteractionProfile = oculusProfilePath,
            CountSuggestedBindings = 6,
            SuggestedBindings = oculusBindings
        };

        var resOculus = _xr.SuggestInteractionProfileBinding(_instance, &oculusSuggestedBindings);
        if (resOculus != Result.Success)
        {
            Trace.TraceWarning($"[{nameof(OpenXrContext)}] Failed to suggest oculus bindings: {resOculus}");
        }

        // Attach action sets to session.
        ActionSet actionSetHandle = _actionSet;
        var attachInfo = new SessionActionSetsAttachInfo
        {
            Type = StructureType.SessionActionSetsAttachInfo,
            Next = null,
            CountActionSets = 1,
            ActionSets = &actionSetHandle
        };

        var resAttach = _xr.AttachSessionActionSets(_session, &attachInfo);
        if (resAttach != Result.Success)
        {
            Trace.TraceError($"[{nameof(OpenXrContext)}] Failed to attach action set: {resAttach}");
            return false;
        }

        // Create action spaces for poses.
        var leftSpaceInfo = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo,
            Next = null,
            Action = _poseAction,
            SubactionPath = _leftHandPath,
            PoseInActionSpace = new Posef
            {
                Orientation = new Quaternionf { W = 1, X = 0, Y = 0, Z = 0 },
                Position = new Vector3f { X = 0, Y = 0, Z = 0 }
            }
        };

        Space leftSpace;
        var resLeftSpace = _xr.CreateActionSpace(_session, &leftSpaceInfo, &leftSpace);
        if (resLeftSpace == Result.Success)
        {
            _leftPoseSpace = leftSpace;
        }

        var rightSpaceInfo = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo,
            Next = null,
            Action = _poseAction,
            SubactionPath = _rightHandPath,
            PoseInActionSpace = new Posef
            {
                Orientation = new Quaternionf { W = 1, X = 0, Y = 0, Z = 0 },
                Position = new Vector3f { X = 0, Y = 0, Z = 0 }
            }
        };

        Space rightSpace;
        var resRightSpace = _xr.CreateActionSpace(_session, &rightSpaceInfo, &rightSpace);
        if (resRightSpace == Result.Success)
        {
            _rightPoseSpace = rightSpace;
        }

        // Create grip pose action spaces.
        var leftGripSpaceInfo = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo,
            Next = null,
            Action = _gripPoseAction,
            SubactionPath = _leftHandPath,
            PoseInActionSpace = new Posef
            {
                Orientation = new Quaternionf { W = 1, X = 0, Y = 0, Z = 0 },
                Position = new Vector3f { X = 0, Y = 0, Z = 0 }
            }
        };

        Space leftGripSpace;
        var resLeftGrip = _xr.CreateActionSpace(_session, &leftGripSpaceInfo, &leftGripSpace);
        if (resLeftGrip == Result.Success)
        {
            _leftGripSpace = leftGripSpace;
        }

        var rightGripSpaceInfo = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo,
            Next = null,
            Action = _gripPoseAction,
            SubactionPath = _rightHandPath,
            PoseInActionSpace = new Posef
            {
                Orientation = new Quaternionf { W = 1, X = 0, Y = 0, Z = 0 },
                Position = new Vector3f { X = 0, Y = 0, Z = 0 }
            }
        };

        Space rightGripSpace;
        var resRightGrip = _xr.CreateActionSpace(_session, &rightGripSpaceInfo, &rightGripSpace);
        if (resRightGrip == Result.Success)
        {
            _rightGripSpace = rightGripSpace;
        }

        return true;
    }

    /// <summary>Gets the native image handle for a specific swap-chain image.</summary>
    public nint GetSwapchainImageHandle(int eyeIndex, int imageIndex) => _swapchainImageHandles[eyeIndex][imageIndex];

    /// <summary>Gets the number of swapchain images for the given eye.</summary>
    public int GetSwapchainImageCount(int eyeIndex) => _swapchainImageHandles[eyeIndex].Length;

    /// <summary>Gets the current acquired swap-chain image index for the given eye.</summary>
    public int GetCurrentImageIndex(int eyeIndex) => _currentSwapchainImageIndex[eyeIndex];

    #endregion

    #region Frame Loop

    /// <summary>
    /// Polls OpenXR events and handles session state transitions.
    /// Must be called every frame.
    /// </summary>
    public void PollEvents()
    {
        while (true)
        {
            var eventData = new EventDataBuffer
            {
                Type = StructureType.EventDataBuffer,
                Next = null
            };

            var result = _xr.PollEvent(_instance, &eventData);
            if (result == Result.EventUnavailable)
            {
                break;
            }
            if (result != Result.Success)
            {
                break;
            }

            if (eventData.Type == StructureType.EventDataSessionStateChanged)
            {
                var sessionEvent = (EventDataSessionStateChanged*)Unsafe.AsPointer(ref eventData);
                _sessionState = sessionEvent->State;
                Debug.WriteLine($"[{nameof(OpenXrContext)}] Session state changed: {_sessionState}");

                switch (_sessionState)
                {
                    case SessionState.Ready:
                    {
                        var beginInfo = new SessionBeginInfo
                        {
                            Type = StructureType.SessionBeginInfo,
                            Next = null,
                            PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo
                        };
                        _xr.BeginSession(_session, &beginInfo);
                        _sessionRunning = true;
                        Debug.WriteLine($"[{nameof(OpenXrContext)}] Session began.");
                        break;
                    }
                    case SessionState.Stopping:
                    {
                        _xr.EndSession(_session);
                        _sessionRunning = false;
                        Debug.WriteLine($"[{nameof(OpenXrContext)}] Session ended.");
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Synchronizes input action states with the runtime.
    /// Should be called each frame after PollEvents.
    /// </summary>
    public void SyncActions()
    {
        if (!_sessionRunning || _actionSet.Handle == 0)
        {
            return;
        }

        var activeActionSet = new ActiveActionSet
        {
            ActionSet = _actionSet,
            SubactionPath = 0
        };

        var syncInfo = new ActionsSyncInfo
        {
            Type = StructureType.ActionsSyncInfo,
            Next = null,
            CountActiveActionSets = 1,
            ActiveActionSets = &activeActionSet
        };

        var res = _xr.SyncAction(_session, &syncInfo);
        if (res != Result.Success && res != Result.SessionLossPending)
        {
            // Log only once in a while to avoid flooding
            var now = DateTime.UtcNow;
            if (now - _lastErrorSyncTimestamp > ErrorSyncPeriod)
            {
                Trace.TraceError($"[{nameof(OpenXrContext)}] SyncActions failed: {res}");
                _lastErrorSyncTimestamp = now;
            }
        }
    }

    private float GetActionStateFloat(Silk.NET.OpenXR.Action action, ulong subactionPath)
    {
        if (!_sessionRunning || action.Handle == 0)
        {
            return 0f;
        }

        var getInfo = new ActionStateGetInfo
        {
            Type = StructureType.ActionStateGetInfo,
            Next = null,
            Action = action,
            SubactionPath = subactionPath
        };

        ActionStateFloat state;
        state.Type = StructureType.ActionStateFloat;
        state.Next = null;

        var res = _xr.GetActionStateFloat(_session, &getInfo, &state);
        if (res == Result.Success)
        {
            bool isActive = state.IsActive != 0;

            // Log when tracking state changes to help debug missing inputs
            if (action.Handle == _pinchAction.Handle)
            {
                if (subactionPath == _leftHandPath && _leftPinchActive != isActive)
                {
                    _leftPinchActive = isActive;
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] Left Pinch Action Active: {isActive}");
                }
                else if (subactionPath == _rightHandPath && _rightPinchActive != isActive)
                {
                    _rightPinchActive = isActive;
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] Right Pinch Action Active: {isActive}");
                }
            }
            else if (action.Handle == _grabAction.Handle)
            {
                if (subactionPath == _leftHandPath && _leftGrabActive != isActive)
                {
                    _leftGrabActive = isActive;
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] Left Grab Action Active: {isActive}");
                }
                else if (subactionPath == _rightHandPath && _rightGrabActive != isActive)
                {
                    _rightGrabActive = isActive;
                    Debug.WriteLine($"[{nameof(OpenXrContext)}] Right Grab Action Active: {isActive}");
                }
            }

            if (isActive)
            {
                return state.CurrentState;
            }
        }

        return 0f;
    }

    private bool GetActionStateBool(Silk.NET.OpenXR.Action action, ulong subactionPath)
    {
        if (!_sessionRunning || action.Handle == 0)
        {
            return false;
        }

        var getInfo = new ActionStateGetInfo
        {
            Type = StructureType.ActionStateGetInfo,
            Next = null,
            Action = action,
            SubactionPath = subactionPath
        };

        ActionStateBoolean state;
        state.Type = StructureType.ActionStateBoolean;
        state.Next = null;

        var res = _xr.GetActionStateBoolean(_session, &getInfo, &state);
        if (res == Result.Success && state.IsActive != 0)
        {
            return state.CurrentState != 0;
        }

        return false;
    }

    /// <summary>
    /// Gets the world-space pose of the given action space relative to the AppSpace.
    /// </summary>
    public bool TryGetActionPose(Space actionSpace, out Posef pose)
    {
        pose = default;
        if (!_sessionRunning || actionSpace.Handle == 0 || _appSpace.Handle == 0)
        {
            return false;
        }

        var spaceLocation = new SpaceLocation
        {
            Type = StructureType.SpaceLocation,
            Next = null
        };

        var res = _xr.LocateSpace(actionSpace, _appSpace, _frameState.PredictedDisplayTime, &spaceLocation);
        if (res == Result.Success && (spaceLocation.LocationFlags & SpaceLocationFlags.PositionValidBit) != 0)
        {
            pose = spaceLocation.Pose;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the world-space poses of the 26 hand joints without allocations. 
    /// The jointsOut pointer must point to an array of at least 26 HandJointLocationEXT elements.
    /// </summary>
    public unsafe bool TryGetHandJoints(bool leftHand, HandJointLocationEXT* jointsOut)
    {
        if (!_hasHandTracking || !_sessionRunning || _handTracking == null || jointsOut == null)
        {
            return false;
        }

        var tracker = leftHand ? _leftHandTracker : _rightHandTracker;
        if (!tracker.HasValue || tracker.Value.Handle == 0)
        {
            return false;
        }

        var locateInfo = new HandJointsLocateInfoEXT
        {
            Type = StructureType.HandJointsLocateInfoExt,
            Next = null,
            BaseSpace = _appSpace,
            Time = _frameState.PredictedDisplayTime
        };

        var locations = new HandJointLocationsEXT
        {
            Type = StructureType.HandJointLocationsExt,
            Next = null,
            IsActive = 0,
            JointCount = 26,
            JointLocations = jointsOut
        };

        var res = _handTracking.LocateHandJoints(tracker.Value, &locateInfo, &locations);
        return res == Result.Success && locations.IsActive != 0;
    }

    /// <summary>
    /// Waits for and begins a new frame. Returns false if the frame should be skipped.
    /// </summary>
    public bool BeginFrame()
    {
        if (!_sessionRunning)
        {
            return false;
        }

        var waitInfo = new FrameWaitInfo
        {
            Type = StructureType.FrameWaitInfo,
            Next = null
        };

        var frameState = new FrameState
        {
            Type = StructureType.FrameState,
            Next = null
        };

        var result = _xr.WaitFrame(_session, &waitInfo, &frameState);
        if (result != Result.Success)
        {
            return false;
        }
        _frameState = frameState;

        var beginInfo = new FrameBeginInfo
        {
            Type = StructureType.FrameBeginInfo,
            Next = null
        };

        result = _xr.BeginFrame(_session, &beginInfo);
        return result == Result.Success;
    }

    /// <summary>
    /// Locates the per-eye views (pose + FOV) for the current frame.
    /// </summary>
    public bool LocateViews()
    {
        var viewLocateInfo = new ViewLocateInfo
        {
            Type = StructureType.ViewLocateInfo,
            Next = null,
            ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
            DisplayTime = _frameState.PredictedDisplayTime,
            Space = _appSpace
        };

        var viewState = new ViewState
        {
            Type = StructureType.ViewState,
            Next = null
        };

        uint viewCount = (uint)_views.Length;
        fixed (View* viewsPtr = _views)
        {
            var result = _xr.LocateView(
                _session, &viewLocateInfo, &viewState, viewCount, &viewCount, viewsPtr);
            return result == Result.Success;
        }
    }

    /// <summary>
    /// Acquires and waits for the next swapchain image for the given eye.
    /// </summary>
    public bool AcquireSwapchainImage(int eyeIndex)
    {
        var acquireInfo = new SwapchainImageAcquireInfo
        {
            Type = StructureType.SwapchainImageAcquireInfo,
            Next = null
        };

        uint index;
        var result = _xr.AcquireSwapchainImage(_swapchains[eyeIndex], &acquireInfo, &index);
        if (result != Result.Success)
        {
            return false;
        }
        _currentSwapchainImageIndex[eyeIndex] = (int)index;

        var waitInfo = new SwapchainImageWaitInfo
        {
            Type = StructureType.SwapchainImageWaitInfo,
            Next = null,
            Timeout = long.MaxValue // XR_INFINITE_DURATION
        };

        result = _xr.WaitSwapchainImage(_swapchains[eyeIndex], &waitInfo);
        return result == Result.Success;
    }

    /// <summary>
    /// Releases the current swap-chain image for the given eye.
    /// IMPORTANT: Ensure all GPU work targeting this image is flushed before calling this.
    /// </summary>
    public bool ReleaseSwapchainImage(int eyeIndex)
    {
        var releaseInfo = new SwapchainImageReleaseInfo
        {
            Type = StructureType.SwapchainImageReleaseInfo,
            Next = null
        };

        var result = _xr.ReleaseSwapchainImage(_swapchains[eyeIndex], &releaseInfo);
        return result == Result.Success;
    }

    /// <summary>
    /// Ends the frame, submitting composition layers to the runtime.
    /// If <paramref name="shouldRender"/> is false, submits an empty frame (headset shows nothing).
    /// </summary>
    public void EndFrame(CompositionLayerProjectionView* projectionViews, int viewCount, bool shouldRender)
    {
        if (!_sessionRunning)
        {
            return;
        }

        if (shouldRender && projectionViews != null && viewCount > 0)
        {
            var projectionLayer = new CompositionLayerProjection
            {
                Type = StructureType.CompositionLayerProjection,
                Next = null,
                LayerFlags = 0,
                Space = _appSpace,
                ViewCount = (uint)viewCount,
                Views = projectionViews
            };

            CompositionLayerBaseHeader* layerPtr = (CompositionLayerBaseHeader*)&projectionLayer;
            CompositionLayerBaseHeader** layersPtr = &layerPtr;

            var endInfo = new FrameEndInfo
            {
                Type = StructureType.FrameEndInfo,
                Next = null,
                DisplayTime = _frameState.PredictedDisplayTime,
                EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                LayerCount = 1,
                Layers = layersPtr
            };

            _xr.EndFrame(_session, &endInfo);
        }
        else
        {
            // Submit empty frame
            var endInfo = new FrameEndInfo
            {
                Type = StructureType.FrameEndInfo,
                Next = null,
                DisplayTime = _frameState.PredictedDisplayTime,
                EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                LayerCount = 0,
                Layers = null
            };

            _xr.EndFrame(_session, &endInfo);
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        for (int i = 0; i < _swapchains.Length; i++)
        {
            if (_swapchains[i].Handle != 0)
            {
                _xr.DestroySwapchain(_swapchains[i]);
            }
        }

        if (_appSpace.Handle != 0)
        {
            _xr.DestroySpace(_appSpace);
        }

        if (_handTracking != null)
        {
            if (_leftHandTracker.HasValue && _leftHandTracker.Value.Handle != 0)
            {
                _handTracking.DestroyHandTracker(_leftHandTracker.Value);
            }
            if (_rightHandTracker.HasValue && _rightHandTracker.Value.Handle != 0)
            {
                _handTracking.DestroyHandTracker(_rightHandTracker.Value);
            }
        }

        if (_leftPoseSpace.Handle != 0) _xr.DestroySpace(_leftPoseSpace);
        if (_rightPoseSpace.Handle != 0) _xr.DestroySpace(_rightPoseSpace);
        if (_actionSet.Handle != 0) _xr.DestroyActionSet(_actionSet);

        if (_session.Handle != 0)
        {
            if (_sessionRunning)
            {
                _xr.EndSession(_session);
                _sessionRunning = false;
            }
            _xr.DestroySession(_session);
        }

        if (_instance.Handle != 0)
        {
            _xr.DestroyInstance(_instance);
        }

        _xr.Dispose();

        Debug.WriteLine($"[{nameof(OpenXrContext)}] Disposed.");
    }

    #endregion
}