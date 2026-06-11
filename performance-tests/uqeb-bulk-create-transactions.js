import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend, Rate } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const USERNAME = __ENV.USERNAME || 'admin';
const PASSWORD = __ENV.PASSWORD || 'Admin@123';
const TEST_COUNT = parseInt(__ENV.TEST_COUNT || '100', 10);
const BATCH_SIZE = parseInt(__ENV.BATCH_SIZE || '10', 10);
const BATCH_SLEEP = parseFloat(__ENV.BATCH_SLEEP || '0.2');

const created = new Counter('transactions_created');
const failed = new Counter('transactions_failed');
const errors400 = new Counter('errors_400');
const errors500 = new Counter('errors_500');
const createDuration = new Trend('create_transaction_duration', true);
const unexpectedFailures = new Rate('unexpected_failures');

let firstFailureBody = null;

function logFirstFailure(res) {
  if (res.status >= 500 && !firstFailureBody) {
    firstFailureBody = res.body;
    console.error(`First 500 response (status ${res.status}): ${res.body}`);
  }
}

const isHeavy = TEST_COUNT >= 10000;
const isMedium = TEST_COUNT >= 1000;

export const options = {
  scenarios: {
    bulk_create: {
      executor: 'shared-iterations',
      vus: Math.min(BATCH_SIZE, TEST_COUNT),
      iterations: TEST_COUNT,
      maxDuration: isHeavy ? '3h' : isMedium ? '1h' : '30m',
    },
  },
  thresholds: {
    errors_500: ['count==0'],
    unexpected_failures: isHeavy ? [] : ['rate<0.01'],
    http_req_failed: isHeavy ? [] : ['rate<0.01'],
    create_transaction_duration: isHeavy ? [] : ['p(95)<2000'],
  },
};

function authHeaders(token) {
  return {
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    tags: { name: 'create_transaction', type: 'write' },
  };
}

function track(res, allow4xx = false) {
  if (res.status >= 500) {
    logFirstFailure(res);
    errors500.add(1);
    unexpectedFailures.add(1);
    failed.add(1);
    return;
  }
  if (res.status === 400) {
    errors400.add(1);
    if (!allow4xx) {
      unexpectedFailures.add(1);
      failed.add(1);
    }
    return;
  }
  if (res.status >= 400) {
    unexpectedFailures.add(1);
    failed.add(1);
    return;
  }
  unexpectedFailures.add(0);
}

export function setup() {
  const loginRes = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ username: USERNAME, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (loginRes.status !== 200) {
    throw new Error(`Login failed: ${loginRes.status} ${loginRes.body}`);
  }
  const token = loginRes.json('token');
  const headers = { Authorization: `Bearer ${token}` };

  const categories = http.get(`${BASE_URL}/api/categories`, { headers }).json();
  const parties = http.get(`${BASE_URL}/api/external-parties`, { headers }).json();
  const departments = http.get(`${BASE_URL}/api/departments`, { headers }).json();

  return {
    token,
    categoryId: categories[0]?.id || 1,
    partyId: parties[0]?.id || 1,
    departmentId: departments[0]?.id || 1,
  };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const incomingNumber = `LOAD-TEST-${Date.now()}-${__VU}-${__ITER}`;
  const useInternal = __ITER % 5 === 0;

  const payload = JSON.stringify({
    incomingNumber,
    incomingDate: new Date().toISOString().split('T')[0] + 'T00:00:00',
    subject: `معاملة اختبار تحمل ${incomingNumber}`,
    incomingSourceType: useInternal ? 'Internal' : 'External',
    incomingFromPartyId: useInternal ? null : data.partyId,
    incomingFromDepartmentId: useInternal ? data.departmentId : null,
    responseType: 'External',
    responseDueDays: 7,
    priority: 'Normal',
    categoryId: data.categoryId,
    notes: 'LOAD-TEST bulk create',
  });

  const res = http.post(`${BASE_URL}/api/transactions`, payload, headers);
  createDuration.add(res.timings.duration);
  track(res);

  const ok = check(res, { 'create 201': (r) => r.status === 201 });
  if (ok) created.add(1);

  if ((__ITER + 1) % BATCH_SIZE === 0) sleep(BATCH_SLEEP);
}

export function handleSummary(data) {
  const createdCount = data.metrics.transactions_created?.values?.count || 0;
  const failedCount = data.metrics.transactions_failed?.values?.count || 0;
  const e400 = data.metrics.errors_400?.values?.count || 0;
  const e500 = data.metrics.errors_500?.values?.count || 0;
  const p95 = data.metrics.create_transaction_duration?.values?.['p(95)'] || 0;
  const avg = data.metrics.create_transaction_duration?.values?.avg || 0;

  const custom = [
    '',
    '=== Bulk Create Summary ===',
    `TEST_COUNT target: ${TEST_COUNT}`,
    `Created: ${createdCount}`,
    `Failed: ${failedCount}`,
    `400 errors: ${e400}`,
    `500 errors: ${e500}`,
    `Avg create ms: ${avg.toFixed(2)}`,
    `p95 create ms: ${p95.toFixed(2)}`,
    firstFailureBody ? `First 500 body: ${firstFailureBody}` : '',
    '',
  ].join('\n');

  return {
    stdout: custom + textSummary(data, { indent: ' ', enableColors: true }),
  };
}
