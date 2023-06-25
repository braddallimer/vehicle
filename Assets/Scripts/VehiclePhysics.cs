using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace Brad.Vehicle
{
    public class VehiclePhysics : MonoBehaviour, IControllable
    {
        #region Public Variables
        [Header("Wheels")]
        public Transform flWheel;
        public Transform frWheel, blWheel, brWheel;
        public float wheelWeight;
        public enum driveType { FrontWheelDrive, BackWheelDrive, FourWheelDrive }
        public driveType wheelDrive = driveType.FourWheelDrive;
        public LayerMask wheelRayMask;
        public float wheelRayDistance = 1;

        [Header("Steering")]
        public float maxTurnAngle;
        public AnimationCurve fWheelGripCurve;
        public AnimationCurve bWheelGripCurve;

        [Header("Suspension")]
        public float suspHeight = .5f;
        public float suspStrength = 10;
        public float suspDampening = 5;

        [Header("Acceleration")]
        public float accelerationSpeed = 5;
        public float brakingSpeed = 5;
        public AnimationCurve wheelTorqueCurve;
        public float maxSpeed = 20;

        #endregion

        #region Private Variables
        // Components
        Rigidbody vehicleRb;
        Transform[] wheelsToSteerAndSuspend;
        Transform[] wheelsToAccelerate;
        IControllable icont;

        // Values
        float accelerationInput = 0;
        float steeringInput = 0;
        Dictionary<string, bool> buttonInputs;

        #region Interface values
        public float verticalInput { get => accelerationInput; set => accelerationInput = value; }
        public float horizontalInput { get => steeringInput; set => steeringInput = value; }
        public Dictionary<string, bool> buttonPressStates { get => buttonInputs; set => buttonInputs = value; }
        #endregion

        #endregion

        #region Monobehaviour

        private void Awake()
        {
            // Cache components
            vehicleRb = GetComponent<Rigidbody>();
            icont = GetComponent<IControllable>();
            icont.buttonPressStates = new Dictionary<string, bool> { };
            wheelsToSteerAndSuspend = new Transform[] { flWheel, frWheel, blWheel, brWheel };

            // Cache wheels to accelerate based on wheel drive type
            switch (wheelDrive)
            {
                case driveType.BackWheelDrive:
                    wheelsToAccelerate = new Transform[] { blWheel, brWheel };
                    break;

                case driveType.FourWheelDrive:
                    wheelsToAccelerate = new Transform[] { flWheel, frWheel, blWheel, brWheel };
                    break;

                case driveType.FrontWheelDrive:
                    wheelsToAccelerate = new Transform[] { flWheel, frWheel };
                    break;

                default:
                    wheelsToAccelerate = new Transform[] { flWheel, frWheel, blWheel, brWheel };
                    break;
            }
        }

        private void Update()
        {
            TurnWheels();

            // Add force to each wheel in wheelsToSteerAndSuspend
            for(int i = 0; i < wheelsToSteerAndSuspend.Length; i++)
            {
                // Cache target wheel
                Transform targetWheel = wheelsToSteerAndSuspend[i];

                if (Physics.Raycast(targetWheel.position, -targetWheel.up, out RaycastHit hit, wheelRayDistance, wheelRayMask))
                {
                    // Apply force
                    Vector3 wheelForce = SuspensionForce(targetWheel, hit) + SteeringForce(targetWheel);
                    vehicleRb.AddForceAtPosition(wheelForce, targetWheel.position, ForceMode.Force);

                    // Apply brake force if button pressed
                    if (icont.GetButtonValue("Jump"))
                    {
                        // Apply brake force
                        Vector3 brakeForce = BrakeForce(targetWheel);
                        vehicleRb.AddForceAtPosition(brakeForce, targetWheel.position, ForceMode.Force);
                    }

                    #region Visualisation
                    // Force visual
                    Debug.DrawLine(targetWheel.position, targetWheel.position + wheelForce, Color.yellow);
                    // Local transform visual
                    VisualiseLocalTransform(targetWheel);
                    #endregion
                }
            }

            // Add force to each wheel in wheelsToAccelerate
            for (int i = 0; i < wheelsToAccelerate.Length; i++)
            {
                // Cache target wheel
                Transform targetWheel = wheelsToAccelerate[i];

                if (Physics.Raycast(targetWheel.position, -targetWheel.up, wheelRayDistance, wheelRayMask))
                {
                    // Apply force
                    Vector3 wheelForce = AccelerationForce(targetWheel);
                    vehicleRb.AddForceAtPosition(wheelForce, targetWheel.position, ForceMode.Force);
                }
            }
        }

        #endregion

        #region Custom Methods

        // Calculate suspension force for this wheel
        Vector3 SuspensionForce(Transform wheelTransform, RaycastHit wheelRayHit)
        {
            // Direction for the suspension force
            Vector3 suspDirection = wheelTransform.up;

            // World velocity of the wheel
            Vector3 wheelVelocity = vehicleRb.GetPointVelocity(wheelTransform.position);

            // Get offset
            float offset = suspHeight - wheelRayHit.distance;

            // Get target velcocity
            float targetVelocity = Vector3.Dot(suspDirection, wheelVelocity);

            // Caculate suspension force
            float suspensionForce = (offset * suspStrength) - (targetVelocity * suspDampening);

            #region Visualisation
            // Raycast visualisation
            Debug.DrawLine(wheelTransform.position, wheelRayHit.collider ? wheelRayHit.point : wheelTransform.position + wheelTransform.up * -suspHeight, Color.white);
            #endregion

            // Return the final force
            return suspDirection * suspensionForce;
        }

        // Calculate steering force for this wheel
        Vector3 SteeringForce(Transform wheelTransform)
        {
            // Direction for the steering force
            Vector3 steerDirection = wheelTransform.right;

            // World velocity of the wheel
            Vector3 wheelVelocity = vehicleRb.GetPointVelocity(wheelTransform.position);

            // Steering velocity
            float steerVelocity = Vector3.Dot(steerDirection, wheelVelocity);

            // Get wheelGripAmount depending on if its a front or back wheel
            float wheelGripAmount = wheelTransform.name.Contains('F') ? fWheelGripCurve.Evaluate(steerVelocity) : bWheelGripCurve.Evaluate(steerVelocity);

            // Target velocity change amount
            float targetVelocityChange = -steerVelocity * wheelGripAmount;

            // Target acceleration
            float targetAcceleration = targetVelocityChange / Time.fixedDeltaTime;

            // Return the final force
            return steerDirection * wheelWeight * targetAcceleration;
        }

        // Calculate acceleration force for this wheel
        Vector3 AccelerationForce(Transform wheelTransform)
        {
            // Direction for the acceleration force
            Vector3 accelDirection = wheelTransform.forward;

            // Only apply force if input is received
            if(Mathf.Abs(accelerationInput) > 0)
            {
                float speed = Vector3.Dot(transform.forward, vehicleRb.velocity);
                float normalisedSpeed = Mathf.Clamp01(Mathf.Abs(speed) / maxSpeed);

                // Limit according to torque
                float torque = wheelTorqueCurve.Evaluate(normalisedSpeed) * accelerationInput;

                // Set wheelForce
                Vector3 accelForce = accelDirection * torque * accelerationSpeed;

                #region Visualisation
                // Force visual
                Debug.DrawLine(wheelTransform.position, wheelTransform.position + accelForce, Color.cyan);
                #endregion

                // Return acceleration force
                return accelForce;
            }
            
            // If no input is detected
            return Vector3.zero;
        }

        Vector3 BrakeForce(Transform wheelTransform)
        {
            // Direction for the acceleration force
            Vector3 brakeDirection = wheelTransform.forward;

            float speed = Vector3.Dot(transform.forward, vehicleRb.velocity);
            float normalisedSpeed = Mathf.Clamp01(Mathf.Abs(speed) / maxSpeed);

            // Only apply force if moving
            if (normalisedSpeed > 0)
            {
                // Limit according to torque
                float torque = wheelTorqueCurve.Evaluate(normalisedSpeed);

                // Set wheelForce
                Vector3 brakeForce = (brakeDirection * Mathf.Sign(speed)) * torque * -brakingSpeed;

                #region Visualisation
                // Force visual
                Debug.DrawLine(wheelTransform.position, wheelTransform.position + brakeForce, Color.red);
                #endregion

                // Return acceleration force
                return brakeForce;
            }

            // If no input is detected
            return Vector3.zero;
        }

        void TurnWheels()
        {
            flWheel.localEulerAngles = new Vector3(0, icont.horizontalInput * maxTurnAngle, 0);
            frWheel.localEulerAngles = new Vector3(0, icont.horizontalInput * maxTurnAngle, 0);
        }

        void VisualiseLocalTransform(Transform targetTransform)
        {
            // Right vector
            Debug.DrawLine(targetTransform.position, targetTransform.position + targetTransform.right, Color.red);

            // Up vector
            Debug.DrawLine(targetTransform.position, targetTransform.position + targetTransform.up, Color.green);

            // Forward vector
            Debug.DrawLine(targetTransform.position, targetTransform.position + targetTransform.forward, Color.blue);
        }

        #endregion
    }
}

