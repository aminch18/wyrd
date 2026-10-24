import source from '../../../../../docs/guides/running-durable.md?raw'
import { MarkdownArticle } from './MarkdownArticle'

export function RunningDurableDoc() {
    return (
        <MarkdownArticle
            source={source}
            eyebrow="Guides · production"
            title="Running durable"
            lead="The same module on PostgreSQL or SQL Server: retention you decide, workers, integrations, operators, health and RabbitMQ between services."
        />
    )
}
