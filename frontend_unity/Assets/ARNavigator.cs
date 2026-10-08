using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AR 导航核心模块
///
/// 职责:
///   1. 接收已解算好的 alignMatrix 和 path(由 ARNetworkManager 传入)
///   2. 每帧计算当前相机到下一节点的方向,驱动箭头朝向
///   3. 到达节点时自动切换下一航点
///
/// 坐标系约定:
///   - path 中的点已经在 ARNetworkManager 中完成尺度恢复和必要的 Y 轴翻转
///   - alignMatrix 的作用是把 "米制 COLMAP 坐标" 变换为 "Unity AR 物理坐标"
/// </summary>
public class ARNavigator : MonoBehaviour
{
    [Header("基础配置")]
    public Transform arCamera;
    public GameObject arrowPrefab;

    [Header("导航参数")]
    [Tooltip("距离节点多近算'到达'并切换下一个节点(米)")]
    public float arriveDistance = 0.8f;

    [Tooltip("箭头相对相机前方的距离(米)")]
    public float arrowForwardDistance = 1.5f;

    [Tooltip("箭头相对相机下方的距离(米),大致对应地面")]
    public float arrowDownOffset = 1.2f;

    [Tooltip("箭头位置跟随平滑系数")]
    public float arrowFollowSmooth = 6f;

    [Tooltip("箭头旋转平滑系数")]
    public float arrowRotationSmooth = 8f;

    [Tooltip("箭头模型本地朝向修正。若真机测试发现整体偏90/180度, 在Inspector里调这个值")]
    public float arrowModelYawOffset = 0f;

    // 运行时状态
    private GameObject currentArrow;
    private List<Vector3> currentPath;
    private int currentTargetIndex = 0;
    private Matrix4x4 savedAlignMatrix;
    private bool isNavigating = false;
    public System.Action OnDestinationReached;


    // ============================================================
    // 对外 API:启动导航
    // path 中的点应该是已完成尺度恢复的米制 COLMAP 坐标
    // alignMatrix 是 "米制 COLMAP 坐标系 → AR 物理世界" 的变换
    // ============================================================
    public void StartNavigation(List<Vector3> path, Matrix4x4 alignMatrix)
    {
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[ARNavigator] 收到空路径,忽略");
            return;
        }
        if (arCamera == null)
        {
            Debug.LogError("[ARNavigator] arCamera 未绑定,无法启动导航");
            return;
        }
        if (arrowPrefab == null)
        {
            Debug.LogError("[ARNavigator] arrowPrefab 未绑定");
            return;
        }

        // 若已有正在导航,先清理
        StopNavigation();

        currentPath = path;
        savedAlignMatrix = alignMatrix;

        // path[0] 通常就是起点(你现在的位置),所以直接看向 path[1]
        currentTargetIndex = path.Count > 1 ? 1 : 0;

        // 生成箭头
        currentArrow = Instantiate(arrowPrefab);
        currentArrow.transform.localScale = Vector3.one * 0.3f;

        isNavigating = true;

        Debug.Log($"[ARNavigator] 导航启动,路径共 {path.Count} 个节点,首个目标索引 {currentTargetIndex}, arriveDistance={arriveDistance:F2}m");
        NavTrace.Log($"ARNavigator StartNavigation pathCount={path.Count}, firstTargetIndex={currentTargetIndex}, arriveDistance={arriveDistance:F2}m");

        // --- 诊断日志: 打印所有节点变换后的 AR 坐标与距相机水平距离 ---
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 worldPos = alignMatrix.MultiplyPoint3x4(path[i]);
            float distToCam = Vector2.Distance(
                new Vector2(arCamera.position.x, arCamera.position.z),
                new Vector2(worldPos.x, worldPos.z)
            );
            Debug.Log($"[PathDebug] node[{i}] metric={path[i]} ar={worldPos} dist={distToCam:F2}m");
            Debug.Log($"[RouteNodeAR] index={i}, metric={path[i]}, ar={worldPos}, arY={worldPos.y:F2}, distToCamera={distToCam:F2}m");
            NavTrace.Log($"RouteNodeAR index={i}, metric={path[i]}, ar={worldPos}, arY={worldPos.y:F2}, distToCamera={distToCam:F2}m");
        }
    }


    // ============================================================
    // 对外 API:手动停止导航(可由外部 UI 调用)
    // ============================================================
    public void StopNavigation()
    {
        isNavigating = false;
        currentPath = null;
        currentTargetIndex = 0;

        if (currentArrow != null)
        {
            Destroy(currentArrow);
            currentArrow = null;
        }
    }


    // ============================================================
    // 每帧更新
    // ============================================================
    void Update()
    {
        if (!isNavigating) return;
        if (currentPath == null || currentArrow == null || arCamera == null) return;
        if (currentTargetIndex >= currentPath.Count) return;

        // 1. 获取当前目标节点的虚拟坐标,通过 alignMatrix 变换到 AR 物理世界
        Vector3 rawTarget = currentPath[currentTargetIndex];
        Vector3 realWorldTarget = savedAlignMatrix.MultiplyPoint3x4(rawTarget);

        // 2. 水平面距离判断(忽略身高差和楼层差)
        Vector2 camPos2D = new Vector2(arCamera.position.x, arCamera.position.z);
        Vector2 targetPos2D = new Vector2(realWorldTarget.x, realWorldTarget.z);
        float distance = Vector2.Distance(camPos2D, targetPos2D);

        // 3. 到达判断
        if (distance < arriveDistance)
        {
            currentTargetIndex++;
            Debug.Log($"[ARNavigator] 到达节点,切换至目标索引 {currentTargetIndex}, 距离={distance:F2}m");
            NavTrace.Log($"Arrived node, nextTargetIndex={currentTargetIndex}, distance={distance:F2}m");

            if (currentTargetIndex >= currentPath.Count)
            {
                Debug.Log("[ARNavigator] 🎉 导航结束,已到达目的地");
                NavTrace.Log("ARNavigator destination reached.");
                OnDestinationReached?.Invoke();
                StopNavigation();
                return;
            }
            // 切节点那一帧先不更新箭头朝向,等下一帧用新目标重新计算
            // (天然地防止了"一帧连跳多个节点"的情况, 因为 return 了)
            return;
        }

        // 4. 更新箭头视觉
        UpdateArrowVisuals(realWorldTarget);
    }


    // ============================================================
    // 箭头位置与朝向
    // ============================================================
    private void UpdateArrowVisuals(Vector3 realWorldTarget)
    {
        // A. 箭头跟随相机:出现在相机前方 + 下方地面附近
        Vector3 spawnPos = arCamera.position + arCamera.forward * arrowForwardDistance;
        spawnPos.y = arCamera.position.y - arrowDownOffset;

        currentArrow.transform.position = Vector3.Lerp(
            currentArrow.transform.position,
            spawnPos,
            Time.deltaTime * arrowFollowSmooth
        );

        // B. 箭头朝向目标:只算水平方向,锁定在地面平面
        Vector3 dirToTarget = realWorldTarget - currentArrow.transform.position;
        dirToTarget.y = 0;

        if (dirToTarget.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dirToTarget, Vector3.up);
            Quaternion modelFix = Quaternion.Euler(0f, arrowModelYawOffset, 0f);
            currentArrow.transform.rotation = Quaternion.Slerp(
                currentArrow.transform.rotation,
                targetRotation * modelFix,
                Time.deltaTime * arrowRotationSmooth
            );
        }
    }


    // ============================================================
    // GameObject 销毁时清理
    // ============================================================
    void OnDestroy()
    {
        StopNavigation();
    }
}
