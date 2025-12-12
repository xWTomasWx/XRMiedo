using DG.Tweening;
using Oculus.Interaction;
using System.Collections.Generic;
using UnityEngine;

public enum InputType
{
    HandTracking,
    Controller
}

public class XRHandDraw : MonoBehaviour
{
    [Header("Input Settings")]

    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
    [SerializeField] private Transform controllerTip;

    [Header("Tracking Settings")]
    [SerializeField] private GameObject trackingHand;

    [Header("Drawing Settings")]
    [SerializeField] private float minFingerPinchStrength = 0.5f;
    [SerializeField] private float minDistanceBeforeNewPoint = 0.004f;
    [SerializeField] private float tubeDefaultWidth = 0.010f;
    [SerializeField] private int tubeSides = 8;
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private Material defaultLineMaterial;

    [Header("Behavior Settings")]
    [SerializeField] private bool enableGravity = false;
    [SerializeField] private bool colliderTrigger = false;

    [Header("Erase Settings")]
    [SerializeField] private float eraseRadius = 0.05f;
    [SerializeField] private LayerMask tubeLayer;
    [SerializeField] private float timeBeforeEraseStarts = 0.5f;
    [SerializeField] private float handMoveSpeedThreshold = 0.01f;

    private float palmOpenTimer = 0f;
    private Vector3 lastHandPosition = Vector3.zero;
    private Vector3 prevHandPoint = Vector3.zero;
    private Vector3 prevControllerPoint = Vector3.zero;

    private List<Vector3> points = new List<Vector3>();
    private List<TubeRenderer> tubeRenderers = new List<TubeRenderer>();
    private TubeRenderer currentTubeRenderer;

    private bool isDrawing = false;
    private OVRHand ovrHand;
    private OVRSkeleton ovrSkeleton;
    private Transform intexfinger;

    private int pointsSinceLastMeshUpdate = 0;
    private int updateMeshEveryNPoints = 5;

    private void Start()
    {
        if (trackingHand != null)
        {
            ovrHand = trackingHand.GetComponent<OVRHand>();
            ovrSkeleton = trackingHand.GetComponent<OVRSkeleton>();
        }
    }


    private void Update()
    {
        // Dynamically choose between hand or controller
        if (ovrHand == null || ovrSkeleton == null)
        {
            ovrHand = trackingHand.GetComponent<OVRHand>();
            ovrSkeleton = trackingHand.GetComponent<OVRSkeleton>();
        }

        bool handTracked = ovrHand.IsTracked &&
                           ovrHand.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High;

        if (handTracked)
        {
            UpdateHandDrawing();
        }
        else
        {
            UpdateControllerDrawing();
        }
    }


    private void UpdateHandDrawing()
    {
        if (ovrSkeleton.Bones != null && intexfinger == null)
        {
            foreach (var b in ovrSkeleton.Bones)
            {
                if (b.Id == OVRSkeleton.BoneId.Hand_IndexTip)
                {
                    intexfinger = b.Transform;
                    break;
                }
            }
        }

        if (intexfinger == null) return;

        bool isPinching = ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        float pinchStrength = ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

        if (ovrHand.GetFingerConfidence(OVRHand.HandFinger.Index) != OVRHand.TrackingConfidence.High)
            return;

        if (isPinching && pinchStrength >= minFingerPinchStrength)
        {
            if (currentTubeRenderer == null) AddNewTubeRenderer();
            UpdateTube(intexfinger.position, ref prevHandPoint);
            isDrawing = true;
        }
        else if (isDrawing)
        {
            FinishDrawing();
        }

        if (IsPalmOpen())
        {
            palmOpenTimer += Time.deltaTime;
            if (palmOpenTimer >= timeBeforeEraseStarts && HandIsMoving())
            {
                TryErase(intexfinger.position);
            }
        }
        else
        {
            palmOpenTimer = 0f;
        }
    }

    private void UpdateControllerDrawing()
    {
        if (controllerTip == null) return;

        bool triggerPressed = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.5f;

        if (triggerPressed)
        {
            if (currentTubeRenderer == null) AddNewTubeRenderer();
            UpdateTube(controllerTip.position, ref prevControllerPoint);
            isDrawing = true;
        }
        else if (isDrawing)
        {
            FinishDrawing();
        }
    }

    private void UpdateTube(Vector3 currentPosition, ref Vector3 prevPoint)
    {
        if (prevPoint == Vector3.zero) prevPoint = currentPosition;

        float dist = Vector3.Distance(prevPoint, currentPosition);
        if (dist >= minDistanceBeforeNewPoint)
        {
            int steps = Mathf.CeilToInt(dist / minDistanceBeforeNewPoint);
            Vector3 start = prevPoint;
            Vector3 end = currentPosition;

            for (int i = 1; i <= steps; i++)
            {
                Vector3 interpolated = Vector3.Lerp(start, end, i / (float)(steps + 1));
                AddPoint(interpolated);
            }

            prevPoint = currentPosition;
            AddPoint(prevPoint);
        }
    }

    private void AddNewTubeRenderer()
    {
        points.Clear();
        prevHandPoint = Vector3.zero;
        prevControllerPoint = Vector3.zero;

        GameObject go = new GameObject($"TubeRenderer__{tubeRenderers.Count}");
        go.transform.position = Vector3.zero;
        go.layer = LayerMask.NameToLayer("Tube");

        TubeRenderer goTubeRenderer = go.AddComponent<TubeRenderer>();
        tubeRenderers.Add(goTubeRenderer);

        var renderer = go.GetComponent<MeshRenderer>();
        Material newMat = new Material(defaultLineMaterial);
        newMat.color = defaultColor;
        renderer.material = newMat;

        goTubeRenderer.ColliderTrigger = colliderTrigger;
        goTubeRenderer.SetPositions(points.ToArray());
        goTubeRenderer._radiusOne = tubeDefaultWidth;
        goTubeRenderer._radiusTwo = tubeDefaultWidth;
        goTubeRenderer._sides = tubeSides;

        currentTubeRenderer = goTubeRenderer;
    }

    private void AddPoint(Vector3 position)
    {
        points.Add(position);
        pointsSinceLastMeshUpdate++;

        if (pointsSinceLastMeshUpdate >= updateMeshEveryNPoints)
        {
            currentTubeRenderer.SetPositions(points.ToArray());
            currentTubeRenderer.GenerateMesh(false);
            pointsSinceLastMeshUpdate = 0;
        }
    }

    private void FinishDrawing()
    {
        if (enableGravity && currentTubeRenderer != null)
        {
            currentTubeRenderer.EnableGravity();
        }
        else if (currentTubeRenderer != null)
        {
            StartFloating(currentTubeRenderer.transform);
        }

        if (currentTubeRenderer != null)
        {
            currentTubeRenderer.GenerateMesh(true);
        }

        currentTubeRenderer = null;
        isDrawing = false;
    }

    private void StartFloating(Transform target)
    {
        if (target == null) return;

        Sequence floatSequence = DOTween.Sequence();
        floatSequence.Append(target.DOLocalMoveX(target.localPosition.x + Random.Range(-0.02f, 0.02f), 3f).SetEase(Ease.InOutSine));
        floatSequence.Join(target.DOLocalMoveY(target.localPosition.y + Random.Range(0.01f, 0.03f), 3f).SetEase(Ease.InOutSine));
        floatSequence.Join(target.DOLocalMoveZ(target.localPosition.z + Random.Range(-0.02f, 0.02f), 3f).SetEase(Ease.InOutSine));
        floatSequence.SetLoops(-1, LoopType.Yoyo);
    }

    private bool IsPalmOpen()
    {
        return !ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index)
            && !ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Middle)
            && !ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Ring)
            && !ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Pinky)
            && !ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Thumb);
    }

    private bool HandIsMoving()
    {
        float handSpeed = (intexfinger.position - lastHandPosition).magnitude / Time.deltaTime;
        lastHandPosition = intexfinger.position;
        return handSpeed > handMoveSpeedThreshold;
    }

    private void TryErase(Vector3 position)
    {
        Collider[] hits = Physics.OverlapSphere(position, eraseRadius, tubeLayer);
        foreach (var hit in hits)
        {
            TubeRenderer tube = hit.GetComponent<TubeRenderer>();
            if (tube != null)
            {
                Destroy(tube.gameObject);
            }
        }
    }

    public void UpdateLineColor(Color color)
    {
        defaultColor = color;

        if (currentTubeRenderer != null)
        {
            var renderer = currentTubeRenderer.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }

    public void UpdateLineWidth(float newValue)
    {
        tubeDefaultWidth = newValue;
    }

    public void UpdateLineMinDistance(float newValue)
    {
        minDistanceBeforeNewPoint = newValue;
    }
}
