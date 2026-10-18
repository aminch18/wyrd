import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'

export function MessagesDoc() {
    return (
        <DocArticle
            eyebrow="Domain model"
            title="Messages & slices"
            lead="Commands, facts, public contracts, and state each have a distinct job."
        >
            <h2>Say what a message means</h2>
            <CodeBlock code={SNIPPETS.messages} title="Messages.cs" />
            <h2>Model state as a closed set</h2>
            <p>
                Immutable union cases make impossible states unrepresentable,
                while <code>Evolve</code> remains an ordinary deterministic
                function.
            </p>
            <CodeBlock code={SNIPPETS.stateUnion} title="Payment.cs" />
            <h2>Decide in one place</h2>
            <p>
                A command has exactly one owning slice. The analyzer catches
                ambiguous ownership at compile time.
            </p>
            <CodeBlock code={SNIPPETS.slice} title="ChargePaymentSlice.cs" />
        </DocArticle>
    )
}
