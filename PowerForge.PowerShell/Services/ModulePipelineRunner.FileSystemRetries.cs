using System;
using System.IO;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    /// <summary>
    /// Moves a release directory atomically while tolerating short-lived filesystem locks.
    /// </summary>
    /// <param name="sourcePath">Completed temporary directory to promote.</param>
    /// <param name="destinationPath">Final durable directory path.</param>
    /// <param name="failureDescription">Release-specific guidance added when all attempts fail.</param>
    /// <param name="moveDirectory">Optional filesystem boundary used by focused tests.</param>
    /// <param name="delay">Optional retry delay boundary used by focused tests.</param>
    /// <param name="maximumAttempts">Maximum number of move attempts.</param>
    /// <param name="initialDelayMs">Initial retry delay in milliseconds.</param>
    internal static void MoveDirectoryWithRetries(
        string sourcePath,
        string destinationPath,
        string failureDescription,
        Action<string, string>? moveDirectory = null,
        Action<int>? delay = null,
        int maximumAttempts = 6,
        int initialDelayMs = 50)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));

        var move = moveDirectory ?? Directory.Move;
        RunFileSystemOperationWithRetries(
            () => move(sourcePath, destinationPath),
            failureDescription,
            $"moving '{sourcePath}' to '{destinationPath}'",
            delay,
            maximumAttempts,
            initialDelayMs);
    }

    /// <summary>
    /// Executes a release filesystem operation with bounded retries for transient Windows locks.
    /// </summary>
    /// <param name="operation">Filesystem operation to execute.</param>
    /// <param name="failureDescription">Release-specific guidance added when all attempts fail.</param>
    /// <param name="operationDescription">Concrete operation description added to diagnostics.</param>
    /// <param name="delay">Optional retry delay boundary used by focused tests.</param>
    /// <param name="maximumAttempts">Maximum number of operation attempts.</param>
    /// <param name="initialDelayMs">Initial retry delay in milliseconds.</param>
    internal static void RunFileSystemOperationWithRetries(
        Action operation,
        string failureDescription,
        string operationDescription,
        Action<int>? delay = null,
        int maximumAttempts = 6,
        int initialDelayMs = 50)
    {
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));
        if (string.IsNullOrWhiteSpace(failureDescription))
            throw new ArgumentException("Failure description is required.", nameof(failureDescription));
        if (string.IsNullOrWhiteSpace(operationDescription))
            throw new ArgumentException("Operation description is required.", nameof(operationDescription));

        var wait = delay ?? System.Threading.Thread.Sleep;
        maximumAttempts = Math.Max(1, maximumAttempts);
        var delayMs = Math.Min(Math.Max(0, initialDelayMs), 2000);
        Exception? last = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
            }

            if (attempt >= maximumAttempts)
            {
                throw new InvalidOperationException(
                    $"{failureDescription} Failed after {maximumAttempts} attempts while {operationDescription}.",
                    last);
            }

            if (delayMs > 0)
                wait(delayMs);
            delayMs = Math.Min(delayMs * 2, 2000);
        }
    }

    /// <summary>
    /// Cleans a temporary release path without hiding an operation failure already in flight.
    /// </summary>
    /// <param name="temporaryPath">Temporary path to clean.</param>
    /// <param name="operationFailure">Primary operation failure, when cleanup runs during exception unwinding.</param>
    /// <param name="cleanup">Cleanup action.</param>
    internal static void CleanupTemporaryPath(
        string temporaryPath,
        Exception? operationFailure,
        Action cleanup)
    {
        if (cleanup is null)
            throw new ArgumentNullException(nameof(cleanup));

        try
        {
            cleanup();
        }
        catch (Exception cleanupFailure) when (operationFailure is not null)
        {
            throw new InvalidOperationException(
                $"{operationFailure.Message} Cleanup also failed for temporary path '{temporaryPath}'; release its filesystem lock and remove the retained temporary path before retrying.",
                new AggregateException(operationFailure, cleanupFailure));
        }
    }
}
