import { Callout } from '../../components/common/Callout'
import { CodeBlock } from '../../components/common/CodeBlock'
import { SNIPPETS } from '../../data/docs'
import { DocArticle } from './DocArticle'

export function TestingDoc() {
    return (
        <DocArticle
            eyebrow="Confidence"
            title="Testing & Native AOT"
            lead="Write business-readable scenarios and ship without runtime type discovery."
        >
            <h2>Given / When / Then for the domain</h2>
            <p>
                <code>Wyrd.Testing</code> exercises slices and projections
                through JSON round trips so tests stay honest about message
                boundaries.
            </p>
            <CodeBlock code={SNIPPETS.testing} title="PaymentTests.cs" />
            <h2>Source-generated serialization</h2>
            <p>
                Production code resolves registered names through your generated{' '}
                <code>JsonSerializerContext</code>; it does not depend on{' '}
                <code>Type.GetType</code> or runtime code generation.
            </p>
            <CodeBlock code={SNIPPETS.aotJson} title="ShopJson.cs" />
            <Callout title="Native AOT is part of CI">
                The Shop console sample is published with Native AOT in CI,
                keeping trim safety a continuously verified property.
            </Callout>
        </DocArticle>
    )
}
