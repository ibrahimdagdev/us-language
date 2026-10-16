using Xunit;

// UsEmitDatabase / UsEmitHttp / UsEmitDi are process-static; Kestrel + DB tests must not interleave.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
