using System;

namespace ZonderqOS.Storage
{
    /// <summary>
    /// Tracks the 9 mandatory installation steps and progress without per-frame heap allocations.
    /// </summary>
    public sealed class InstallerStepTracker
    {
        public const int StepCount = 9;

        public const int StatePending = 0;
        public const int StateActive = 1;
        public const int StateCompleted = 2;
        public const int StateFailed = 3;

        private static readonly string[] StepTitles = new string[StepCount]
        {
            "Preparing disk",
            "Creating partitions",
            "Formatting filesystem",
            "Installing bootloader",
            "Copying system files",
            "Configuring boot slots",
            "Setting up user account",
            "Verifying installation",
            "Finishing installation"
        };

        private readonly int[] stepStates = new int[StepCount];

        public int CurrentStepIndex { get; private set; }
        public int ProgressPercent { get; private set; }
        public bool IsCompleted { get; private set; }
        public bool HasFailed { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;
        public string CurrentStepName =>
            (CurrentStepIndex >= 0 && CurrentStepIndex < StepCount)
                ? StepTitles[CurrentStepIndex]
                : "Ready";

        public InstallerStepTracker()
        {
            Reset();
        }

        public static string GetStepTitle(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= StepCount)
                return string.Empty;
            return StepTitles[stepIndex];
        }

        public int GetStepState(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= StepCount)
                return StatePending;
            return stepStates[stepIndex];
        }

        public void Reset()
        {
            for (int i = 0; i < StepCount; i++)
                stepStates[i] = StatePending;

            CurrentStepIndex = 0;
            ProgressPercent = 0;
            IsCompleted = false;
            HasFailed = false;
            ErrorMessage = string.Empty;
        }

        public void BeginStep(int stepIndex, int progressPercent)
        {
            if (HasFailed || stepIndex < 0 || stepIndex >= StepCount)
                return;

            for (int i = 0; i < stepIndex; i++)
            {
                if (stepStates[i] == StateActive || stepStates[i] == StatePending)
                    stepStates[i] = StateCompleted;
            }

            CurrentStepIndex = stepIndex;
            stepStates[stepIndex] = StateActive;
            UpdateStepProgress(progressPercent);
        }

        public void UpdateStepProgress(int progressPercent)
        {
            if (HasFailed)
                return;

            if (progressPercent < 0) progressPercent = 0;
            if (progressPercent > 100) progressPercent = 100;
            if (progressPercent > ProgressPercent)
                ProgressPercent = progressPercent;
        }

        public void CompleteStep(int stepIndex)
        {
            if (HasFailed || stepIndex < 0 || stepIndex >= StepCount)
                return;

            stepStates[stepIndex] = StateCompleted;
        }

        public void Fail(string errorMessage)
        {
            HasFailed = true;
            IsCompleted = false;
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? "Installation failed due to an unexpected error."
                : errorMessage;

            if (CurrentStepIndex >= 0 && CurrentStepIndex < StepCount)
                stepStates[CurrentStepIndex] = StateFailed;
        }

        public void CompleteAll()
        {
            if (HasFailed)
                return;

            for (int i = 0; i < StepCount; i++)
                stepStates[i] = StateCompleted;

            CurrentStepIndex = StepCount - 1;
            ProgressPercent = 100;
            IsCompleted = true;
        }
    }
}
