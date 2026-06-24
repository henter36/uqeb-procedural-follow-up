import http from 'k6/http';
import { check, sleep } from 'k6';
import { login, requireCredentials } from '../../helpers/auth.js';
import { apiReadSmokeBaseline } from './helpers/baseline-thresholds.js';

const apiBase = __ENV.UQEB_API_BASE_URL || 'http://localhost:5000/api';
const username = __ENV.UQEB_TEST_USERNAME;
const password = __ENV.UQEB_TEST_PASSWORD;

export const options = {
  vus: Number(__ENV.K6_VUS || apiReadSmokeBaseline.defaults.vus),
  duration: __ENV.K6_DURATION || apiReadSmokeBaseline.defaults.duration,
  thresholds: apiReadSmokeBaseline.thresholds,
  tags: {
    baselineScenario: apiReadSmokeBaseline.scenarioId,
  },
};

export function setup() {
  requireCredentials(username, password);
  return {
    token: login(apiBase, username, password),
  };
}

export default function readSmokeBaseline(data) {
  const headers = {
    Authorization: `Bearer ${data.token}`,
    'Content-Type': 'application/json',
  };

  const dashboard = http.get(`${apiBase}/dashboard`, {
    headers,
    tags: { endpoint: 'dashboard' },
  });
  check(dashboard, { 'dashboard 200': (r) => r.status === 200 });

  const transactions = http.get(`${apiBase}/transactions?page=1&pageSize=10`, {
    headers,
    tags: { endpoint: 'transactions-list' },
  });
  check(transactions, { 'transactions 200': (r) => r.status === 200 });

  const items = transactions.json('items');
  if (Array.isArray(items) && items.length > 0) {
    const id = items[0].id;
    const detail = http.get(`${apiBase}/transactions/${id}/basic`, {
      headers,
      tags: { endpoint: 'transaction-basic' },
    });
    check(detail, { 'transaction basic 200': (r) => r.status === 200 });
  }

  sleep(1);
}

export function handleSummary(data) {
  const p95 = data.metrics.http_req_duration?.values?.['p(95)'] ?? null;
  const failedRate = data.metrics.http_req_failed?.values?.rate ?? null;

  return {
    stdout: [
      `baselineScenario=${apiReadSmokeBaseline.scenarioId}`,
      `httpReqDurationP95Ms=${p95}`,
      `httpReqFailedRate=${failedRate}`,
      'Copy these values into artifacts/performance-baseline/ using docs/performance_baseline/records/api-read-smoke.template.json',
    ].join('\n'),
  };
}
