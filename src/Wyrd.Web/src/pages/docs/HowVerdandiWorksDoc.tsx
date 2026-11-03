import source from '../../../../../docs/guides/how-verdandi-works.md?raw'
import { MarkdownArticle } from './MarkdownArticle'

export function HowVerdandiWorksDoc() {
    return (
        <MarkdownArticle
            source={source}
            eyebrow="Guides · architecture"
            title="How Verdandi works"
            lead="Underneath the durable host: where the data lives, one message as one transaction, workers, duplicates, failures, concurrency limits and cleanup."
        />
    )
}
