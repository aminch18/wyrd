import type { ComponentType } from 'react'
import { QuickStartDoc } from './QuickStartDoc'
import { OverviewDoc } from './OverviewDoc'
import { MessagesDoc } from './MessagesDoc'
import { ProcessesDoc } from './ProcessesDoc'
import { ContractsDoc } from './ContractsDoc'
import { TestingDoc } from './TestingDoc'
import { RoadmapDoc } from './RoadmapDoc'
import { ToolDoc } from './ToolDoc'

export const DOC_COMPONENTS: Record<string, ComponentType> = {
    'quick-start': QuickStartDoc,
    overview: OverviewDoc,
    messages: MessagesDoc,
    processes: ProcessesDoc,
    contracts: ContractsDoc,
    tool: ToolDoc,
    'testing-aot': TestingDoc,
    roadmap: RoadmapDoc,
}
