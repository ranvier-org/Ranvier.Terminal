# Composition and component benchmarks

Measured here on Windows in Release with Ranvier tracing disabled, using
`dotnet WidgetBench.dll --components`. Ran the before and final binaries in three
pairs, reversing their order in the middle pair. Each timing is the median of the
three runs' warmed batch medians. Allocation is the median managed bytes per
iteration on the benchmark thread.

| Existing workload | Before (µs) | After (µs) | Before / after (bytes) |
| --- | ---: | ---: | ---: |
| Spinner tick through presentation to a null sink | 1.358 | 1.334 | 0 / 0 |
| Finished task tick through idle presentation | 0.008 | 0.008 | 0 / 0 |
| Changing FIGlet heading through null output | 8.640 | 8.056 | 3367 / 3367 |
| Reverse sort and order 1,000 integers | 58.307 | 58.494 | 4200 / 4200 |
| Read the cached sort order | 0.003 | 0.003 | 0 / 0 |

Measured here, the paired collection workload alternated between two prebuilt arrays
containing 100 and 101 items. Each item built a text node with a reactive binding. Both
cases timed signal publication and resulting tree updates, excluding layout and painting.

| Collection operation | Median (µs) | Bytes per change |
| --- | ---: | ---: |
| Rebuild using `Dsl.each` | 16.550 | 122684 |
| Retain surviving rows using `Dsl.eachKeyed` | 2.050 | 4504 |
