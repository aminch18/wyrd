import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { DocArticle } from './DocArticle'

const ASYNCAPI_COMMAND = `dotnet run --project src/Wyrd.Tool -- asyncapi \\
  --contracts samples/Shop/contracts.wyrd.txt \\
  --title "Shop integration events" \\
  --version 0.2.0 \\
  --output samples/Shop/asyncapi.yaml`

const DIAGRAM_COMMAND = `dotnet build samples/Shop

dotnet run --project src/Wyrd.Tool -- diagram \\
  --assembly samples/Shop/bin/Debug/net10.0/Shop.dll \\
  --output docs/shop-processes.md`

export function ToolDoc() {
    return (
        <DocArticle
            eyebrow="Developer tools"
            title="The Wyrd CLI"
            lead="Generate AsyncAPI contracts and Mermaid process diagrams from the same source your application uses."
        >
            <h2>Run it from the repository</h2>
            <p>
                <code>Wyrd.Tool</code> is a .NET tool with the command name{' '}
                <code>wyrd</code>. The examples below run the current source
                directly from the repository root with the .NET 10 SDK, so they
                do not depend on a published tool package.
            </p>
            <CodeBlock
                language="shellscript"
                title="Terminal · repository root"
                code={`dotnet run --project src/Wyrd.Tool -- --help
dotnet run --project src/Wyrd.Tool -- asyncapi --help
dotnet run --project src/Wyrd.Tool -- diagram --help`}
            />
            <h2>Export integration contracts</h2>
            <p>
                <code>asyncapi</code> reads the reviewed{' '}
                <code>contracts.wyrd.txt</code> snapshot and writes an AsyncAPI
                3.0 YAML document. It needs no running application or compiled
                domain assembly. Message names, versions, and payload schemas
                come from the snapshot.
            </p>
            <CodeBlock
                language="shellscript"
                title="Generate AsyncAPI"
                code={ASYNCAPI_COMMAND}
            />
            <p>
                <code>--contracts</code> defaults to{' '}
                <code>contracts.wyrd.txt</code>. <code>--title</code> defaults
                to the snapshot folder name and <code>--version</code> defaults
                to <code>0.0.0</code>. Use these options to describe the API you
                are exporting.
            </p>
            <h2>Draw the process model</h2>
            <p>
                <code>diagram</code> loads a compiled assembly and discovers
                public static fields and properties that expose Wyrd processes.
                It writes a Markdown document containing Mermaid diagrams,
                including steps, retries, awaits, timeouts, and compensation.
            </p>
            <CodeBlock
                language="shellscript"
                title="Generate process diagrams"
                code={DIAGRAM_COMMAND}
            />
            <Callout title="Use an assembly you trust">
                Loading the assembly evaluates its public static process
                members. Build your project first and point the tool at your own
                compiled domain assembly with its dependencies alongside it.
            </Callout>
            <h2>Use it in your workflow</h2>
            <p>
                Both commands accept <code>--output</code> or <code>-o</code>.
                Omit it to write the generated document to standard output;
                diagnostics go to standard error. Invalid input returns a
                nonzero exit code, so a CI job can fail immediately.
            </p>
            <p>
                Generate the files during CI or commit them alongside the model
                for review. AsyncAPI describes the public integration boundary;
                Mermaid describes the compiled business process. They are
                generated documentation, not a replacement for contract
                compatibility checks.
            </p>
            <Callout title="Available today">
                AsyncAPI export and process diagrams work today.
                Process-instance capture and <code>wyrd replay</code> are
                planned and are not CLI commands in the current version.
            </Callout>
        </DocArticle>
    )
}
