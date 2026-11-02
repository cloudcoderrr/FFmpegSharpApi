using Xunit;

// Media tests share the process-global FFmpeg logger and decoders; run them sequentially
// so per-test log capture is deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
