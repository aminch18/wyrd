import type { ComponentType } from 'react'
import { QuickStartDoc } from './QuickStartDoc'
import { OverviewDoc } from './OverviewDoc'
import { ReadModelsDoc } from './ReadModelsDoc'
import { RunningDurableDoc } from './RunningDurableDoc'
import { HowVerdandiWorksDoc } from './HowVerdandiWorksDoc'
import { ApiReferenceDoc } from './ApiReferenceDoc'
import { FromDddDoc } from './FromDddDoc'
import { MessagesDoc } from './MessagesDoc'
import { ProcessesDoc } from './ProcessesDoc'
import { ContractsDoc } from './ContractsDoc'
import { TestingDoc } from './TestingDoc'
import { RoadmapDoc } from './RoadmapDoc'
import { SimulationDoc } from './SimulationDoc'
import { ToolDoc } from './ToolDoc'
import { SemanticsDoc } from './SemanticsDoc'
import { DecisionsDoc } from './DecisionsDoc'

export const DOC_COMPONENTS: Record<string, ComponentType> = {
    'quick-start': QuickStartDoc,
    overview: OverviewDoc,
    semantics: SemanticsDoc,
    decisions: DecisionsDoc,
    'from-ddd': FromDddDoc,
    'read-models': ReadModelsDoc,
    'running-durable': RunningDurableDoc,
    'how-verdandi-works': HowVerdandiWorksDoc,
    'api-reference': ApiReferenceDoc,
    messages: MessagesDoc,
    processes: ProcessesDoc,
    simulation: SimulationDoc,
    contracts: ContractsDoc,
    tool: ToolDoc,
    'testing-aot': TestingDoc,
    roadmap: RoadmapDoc,
}
