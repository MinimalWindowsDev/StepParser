global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using Xunit;

// StepLogger and the Fody-woven [Log] aspects hold process-wide static state, so tests that touch
// logging would race against every other test. The suite is small and fast; serialising it buys
// determinism, and a flaky suite is worse than a slow one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
