import readSmoke, { setup } from '../read-smoke.js';
import { apiReadSmokeBaseline } from './helpers/baseline-thresholds.js';

export { setup };

export const options = {
  vus: Number(__ENV.K6_VUS || apiReadSmokeBaseline.defaults.vus),
  duration: __ENV.K6_DURATION || apiReadSmokeBaseline.defaults.duration,
  thresholds: apiReadSmokeBaseline.thresholds,
  tags: {
    baselineScenario: apiReadSmokeBaseline.scenarioId,
  },
};

export default readSmoke;

export function handleSummary(data) {
  const p95 = data.metrics.http_req_duration?.values?.['p(95)'] ?? null;
  const failedRate = data.metrics.http_req_failed?.values?.rate ?? null;

  return {
    stdout: [
      `baselineScenario=${apiReadSmokeBaseline.scenarioId}`,
      `httpReqDurationP95Ms=${p95}`,
      `httpReqFailedRate=${failedRate}`,
      'Copy these values into artifacts/performance-baseline/ using docs/performance_baseline/records/baseline-records.template.json',
    ].join('\n'),
  };
}
