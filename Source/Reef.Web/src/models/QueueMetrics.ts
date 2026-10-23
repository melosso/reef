export interface QueueMetrics {
  queueDepth: number;
  activeJobs: number;
  availableSlots: number;
  maxConcurrentJobs: number;
  totalDequeued: number;
}
