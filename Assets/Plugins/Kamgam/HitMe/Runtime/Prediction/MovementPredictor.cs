using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace Kamgam.HitMe
{
    /// <summary>
    /// The PredictPosition() method is currently implemented as a simulation (see "while (f > 0)").
    /// TODO: It could be converted to a mathematical formula. The angular velocity basically results 
    /// in circles and the linear velocity in lines.
    /// </summary>
    [AddComponentMenu("Hit Me/Movement Predictor")]
    public class MovementPredictor : MonoBehaviour, IMovementPredictor
    {
        public class Entry
        {
            public float Time;
            public float DeltaTime;
            public Vector3 Position;
            public Quaternion Rotation;

            public Entry SetValues(float time, float deltaTime, Vector3 position, Quaternion rotation)
            {
                Time = time;
                DeltaTime = deltaTime;
                Position = position;
                Rotation = rotation;

                return this;
            }
        }

        /// <summary>
        /// The time range that is used for averaging the linear and angular velocities.
        /// </summary>
        [Tooltip("The time range that is used for averaging the linear and angular velocities.")]
        public float TimeRange = 0.5f;
        public bool UseAverageMovement = false;
        public bool UseAngularPrediction = true;

        protected Queue<Entry> _entries = new Queue<Entry>(30);
        protected Stack<Entry> _pool = new Stack<Entry>(30);

        // Current time (incremented in update by Time.deltaTime).
        protected float _time = 0f;

        // Movement vector from last frame to current and positions of past frames.
        protected Vector3 _movementInLastFrame;
        protected float _lastFrameDuration;
        protected Vector3 _lastPos;
        protected Vector3 _lastButOnePos;

        // Result storage
        protected int _predictedPositionForFrame;
        protected float _predictedTimeDelta;
        protected Vector3 _predictedPosition;

        /// <summary>
        /// (optional) The position will be updated with the predicted position.
        /// </summary>
        public GameObject DebugObj;

        public void Awake()
        {
            _predictedPosition = transform.position;
        }

        // We use LateUpdate so all transform changes are done when we take our measurements.
        public void LateUpdate()
        {
            _lastFrameDuration = Time.deltaTime;

            // Time
            _time += Time.deltaTime;

            // Postition
            var pos = transform.position;
            
            // Rotation
            _movementInLastFrame = pos - _lastPos;

            var from = _lastPos - _lastButOnePos;
            var to = pos - _lastPos;

            //Debug.Log( "Magn.: " + (pos - _lastPos).magnitude / Time.deltaTime );

            _lastButOnePos = _lastPos;
            _lastPos = pos;

            var rotation = Quaternion.FromToRotation(from, to);

            // Enqueue
            Enqueue(_time, Time.deltaTime, pos, rotation);

            // Check if the angle change is very big. If yes, then clear the queue.
            rotation.ToAngleAxis(out float angle, out _);
            if (angle > 45f)
            {
                ClearQueue();
            }

            while(_entries.Count > 0 && _time - _entries.Peek().Time > TimeRange)
            {
                Dequeue();
            }
        }

        protected Entry Enqueue(float time, float deltaTime, Vector3 position, Quaternion rotation)
        {
            if(_pool.Count == 0)
            {
                _pool.Push(new Entry());
            }

            var entry = _pool.Pop();
            entry.SetValues(time, deltaTime, position, rotation);

            _entries.Enqueue(entry);

            return entry;
        }

        protected Entry Dequeue()
        {
            var entry = _entries.Dequeue();
            _pool.Push(entry);
            return entry;
        }

        public void ClearQueue()
        {
            while(_entries.Count > 0)
            {
                Dequeue();
            }
        }

        public void ToggleUseAverageMovement()
        {
            UseAverageMovement = !UseAverageMovement;
        }


        public void ToggleAngularPrediction()
        {
            UseAngularPrediction = !UseAngularPrediction;
        }

        public Vector3 PredictPosition(float deltaTimeInSec)
        {
            // Shortcut if we have already predicted the current frame
            if(Time.frameCount == _predictedPositionForFrame && Mathf.Approximately(_predictedTimeDelta, deltaTimeInSec))
            {
                return _predictedPosition;
            }

            float frames = _entries.Count;

            // Abort if predictions are not yet possible.
            // Min 2 frames needed to calc the position average.
            // Min 3 frames needed to calc the angle average.
            if (frames < 3)
                return transform.position;

            if (_entries.Count < 3)
                return transform.position;

            // Calc average position change magnitude
            float avgMagnitudePerSec = 0f;
            Vector3 avgMovementPerSec = Vector3.zero;
            float avgSqrPositionChange = 0f;
            Entry previousEntry = null;
            foreach (var entry in _entries)
            {
                if(previousEntry != null)
                {
                    float deltaTime = entry.Time - previousEntry.Time;
                    //Debug.Log(deltaTime + ", " + entry.DeltaTime + " m: " + (entry.Position - previousEntry.Position).magnitude / entry.DeltaTime);
                    avgSqrPositionChange += (entry.Position - previousEntry.Position).sqrMagnitude;
                    avgMovementPerSec += (entry.Position - previousEntry.Position) / entry.DeltaTime;
                    avgMagnitudePerSec += (entry.Position - previousEntry.Position).magnitude / entry.DeltaTime;
                }
                previousEntry = entry;
            }

            avgSqrPositionChange /= _entries.Count - 1;
            avgMovementPerSec /= _entries.Count - 1;
            avgMagnitudePerSec /= _entries.Count - 1;
            float avgPositionChangeMagnitude = Mathf.Sqrt(avgSqrPositionChange);

            // Skip prediction if the displacement is almost zero.
            if (avgPositionChangeMagnitude < 0.001f)
            {
                return transform.position;
            }

            // Calc average angle change
            Quaternion avgRotation = Quaternion.identity;
            if (UseAngularPrediction)
                avgRotation = AverageQuaternions(_entries);

            // Simulate the movement for the delta time range
            var newPosition = transform.position;
            float averageFrameDuration = (_time - _entries.Peek().Time) / _entries.Count;
            Vector3 movementForNewFrame;
            if (UseAverageMovement)
                movementForNewFrame = avgMovementPerSec * averageFrameDuration;
            else
            {
                movementForNewFrame = avgMagnitudePerSec * _movementInLastFrame.normalized * averageFrameDuration;
                //movementForNewFrame = _movementInLastFrame / _lastFrameDuration * averageFrameDuration;
            }

            float framesToCalculate = deltaTimeInSec / averageFrameDuration;
            int tFrames = Mathf.FloorToInt(framesToCalculate);
            int f = tFrames;
            while (f > 0)
            {
                f--;
                if (UseAngularPrediction)
                    movementForNewFrame = avgRotation * movementForNewFrame;

                // Debug.DrawLine(newPosition, newPosition + movementForNewFrame, Color.blue, 0f);
                newPosition += movementForNewFrame;
            }
            // Interpolate the missing frame fraction (caused by Mathf.FloorToInt above).
            float frameFraction = framesToCalculate - tFrames;
            if (UseAngularPrediction)
                movementForNewFrame = avgRotation * movementForNewFrame;
            newPosition += movementForNewFrame * frameFraction;

            // Set pos of debug obj
            if (DebugObj)
            {
                DebugObj.transform.position = newPosition;
            }

            _predictedPositionForFrame = Time.frameCount;
            _predictedTimeDelta = deltaTimeInSec;
            _predictedPosition = newPosition;

            return newPosition;
        }

        // Thanks to: https://forum.unity.com/threads/average-quaternions.86898/#post-8962665
        Quaternion AverageQuaternions(Queue<Entry> entries)
        {
            Assert.IsTrue(entries != null && entries.Count > 0);

            Vector3 forwardSum = Vector3.zero;
            Vector3 upwardSum = Vector3.zero;
            foreach (var entry in entries)
            {
                forwardSum += entry.Rotation * Vector3.forward;
                upwardSum += entry.Rotation * Vector3.up;
            }
            int count = entries.Count;
            forwardSum /= (float)count;
            upwardSum /= (float)count;

            return Quaternion.LookRotation(forwardSum, upwardSum);
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(MovementPredictor))]
    public class MovementPredictorEditor : UnityEditor.Editor
    {
        MovementPredictor obj;

        public void OnEnable()
        {
            obj = target as MovementPredictor;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Clear"))
            {
                obj.ClearQueue();
            }

            if (GUILayout.Button("Toggle Average Movement: is " + (obj.UseAverageMovement ? "ON" : "off")))
            {
                obj.ToggleUseAverageMovement();
            }

            if (GUILayout.Button("Toggle Angular: is " + (obj.UseAngularPrediction ? "ON" : "off")))
            {
                obj.ToggleAngularPrediction();
            }
        }
    }
#endif
}