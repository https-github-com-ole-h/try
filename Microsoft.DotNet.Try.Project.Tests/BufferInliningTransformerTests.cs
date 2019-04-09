﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Try.Project.Generators;
using Microsoft.DotNet.Try.Project.Transformations;
using MLS.Agent;
using MLS.Protocol.Execution;
using Xunit;

namespace Microsoft.DotNet.Try.Project.Tests
{
    public class BufferInliningTransformerTests
    {

        [Fact]
        public void When_workspace_is_null_then_the_transformer_throw_exception()
        {
            var processor = new BufferInliningTransformer();
            Func<Task> extraction = () => processor.TransformAsync(null);
            extraction.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public async Task Processed_workspace_files_are_modified_inlining_buffers()
        {
            var original = new Workspace(
                files: new[]
                {
                    new Workspace.File("Program.cs", MLS.TestSupport.SourceCodeProvider.ConsoleProgramSingleRegion)
                },
                buffers: new[]
                {
                    new Workspace.Buffer("Program.cs@alpha", "var newValue = 1000;".EnforceLF())
                });
            var processor = new BufferInliningTransformer();

            var processed = await processor.TransformAsync(original);
            processed.Should().NotBeNull();
            processed.Files.Should().NotBeEmpty();
            var newCode = processed.Files.ElementAt(0).Text;

            newCode.Should().NotBe(original.Files.ElementAt(0).Text);
            newCode.Should().Contain("var newValue = 1000;");

            original.Buffers.ElementAt(0).Position.Should().Be(0);
            processed.Buffers.Length.Should().Be(original.Buffers.Length);
            processed.Buffers.ElementAt(0).Position.Should().Be(original.Buffers.ElementAt(0).Position);
            processed.Buffers.ElementAt(0).AbsolutePosition.Should().Be(168);

        }

        [Fact]
        public async Task Processed_workspace_files_are_replaced_by_buffer_when_id_is_just_file_name()
        {
            var ws = new Workspace(
                files: new[]
                {
                    new Workspace.File("Program.cs", MLS.TestSupport.SourceCodeProvider.ConsoleProgramSingleRegion)
                },
                buffers: new[]
                {
                    new Workspace.Buffer("Program.cs", "var newValue = 1000;", 0)
                });
            var processor = new BufferInliningTransformer();

            var processed = await processor.TransformAsync(ws);
            processed.Should().NotBeNull();
            processed.Files.Should().NotBeEmpty();
            var newCode = processed.Files.ElementAt(0).Text;

            newCode.Should().NotBe(ws.Files.ElementAt(0).Text);
            newCode.Should().Be("var newValue = 1000;");

            processed.Buffers.Length.Should().Be(ws.Buffers.Length);
            processed.Buffers.ElementAt(0).Position.Should().Be(0);
        }

        [Fact]
        public async Task Processed_workspace_with_single_buffer_with_empty_id_generates_a_program_file()
        {
            var ws = new Workspace(
                buffers: new[]
                {
                    new Workspace.Buffer("", MLS.TestSupport.SourceCodeProvider.ConsoleProgramSingleRegion, 0)
                });
            var processor = new BufferInliningTransformer();

            var processed = await processor.TransformAsync(ws);
            processed.Should().NotBeNull();
            processed.Files.Should().NotBeEmpty();
            var newCode = processed.Files.ElementAt(0).Text;
            newCode.Should().Contain(MLS.TestSupport.SourceCodeProvider.ConsoleProgramSingleRegion);

        }

        [Fact]
        public async Task If_workspace_contains_with_multiple_buffers_targeting_single_file_generates_a_single_file()
        {
            var expectedCode = @"using System;

namespace ConsoleProgramSingleRegion
{
    public class Program
    {
        public static void Main(string[] args)
        {
            #region alpha
var newValueA = ""as string"";
Console.Write(newValueA);
#endregion

            #region beta
var newValueB = ""as different string"";
Console.Write(newValueA + newValueB);
#endregion
        }
    }
}".EnforceLF();

            var ws = new Workspace(
                files: new[]
                {
                    new Workspace.File("Program.cs", MLS.TestSupport.SourceCodeProvider.ConsoleProgramMultipleRegions)
                },
                buffers: new[]
                {
                    new Workspace.Buffer(new BufferId("Program.cs", "alpha"), "var newValueA = \"as string\";\nConsole.Write(newValueA);", 0),
                    new Workspace.Buffer(new BufferId("Program.cs", "beta"), "var newValueB = \"as different string\";\nConsole.Write(newValueA + newValueB);", 0)
                });
            var processor = new BufferInliningTransformer();

            var processed = await processor.TransformAsync(ws);
            processed.Should().NotBeNull();
            processed.Files.Should().NotBeEmpty();
            var newCode = processed.Files.ElementAt(0).Text.EnforceLF();
            newCode.Should().Be(expectedCode);
        }

        [Fact]
        public async Task If_workspace_contains_files_whose_names_are_absolute_paths_and_they_have_no_content_then_the_contents_are_read_from_disk()
        {
            using (var directory = DisposableDirectory.Create())
            {
                var filePath = Path.Combine(directory.Directory.FullName, "Program.cs");
                var content =
@"using System;";
                File.WriteAllText(filePath, content);
                var ws = new Workspace(
                   files: new[]
                   {
                    new Workspace.File(filePath, null)
                   }
                   );

                var processor = new BufferInliningTransformer();
                var processed = await processor.TransformAsync(ws);
                processed.Files[0].Text.Should().Be(content);
            }
        }

        [Fact]
        public async Task If_workspace_contains_buffers_whose_file_names_are_absolute_paths_the_contents_are_read_from_disk()
        {
            using (var directory = DisposableDirectory.Create())
            {
                var filePath = Path.Combine(directory.Directory.FullName, "Program.cs");
                var fileContent =
                    @"using System;
namespace Code{{
    public static class Program{{
        public static void Main(){{
        #region region one
        #endregion
        }}
    }}
}}".EnforceLF();
                var expectedFileContent =
                    @"using System;
namespace Code{{
    public static class Program{{
        public static void Main(){{
        #region region one
Console.Write(2);
#endregion
        }}
    }}
}}".EnforceLF();

                File.WriteAllText(filePath, fileContent);
                var ws = new Workspace(
                    buffers: new[]
                    {
                        new Workspace.Buffer(new BufferId(filePath,"region one"), "Console.Write(2);"), 
                    }
                );

                var processor = new BufferInliningTransformer();
                var processed = await processor.TransformAsync(ws);
                processed.Files[0].Text.EnforceLF().Should().Be(expectedFileContent);
            }
        }
    }
}