import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const USERNAME = __ENV.USERNAME || 'admin';
const PASSWORD = __ENV.PASSWORD || 'Admin@123';
const VUS = parseInt(__ENV.VUS || '10', 10);
const DURATION = __ENV.DURATION || '2m';

const errors500 = new Counter('errors_500');
const unexpectedFailures = new Rate('unexpected_failures');

export const options = {
  scenarios: {
    read_load: {
      executor: 'constant-vus',
      vus: VUS,
      duration: DURATION,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    errors_500: ['count==0'],
    'http_req_duration{type:read}': ['p(95)<1000'],
    unexpected_failures: ['rate<0.01'],
  },
};

function track(res) {
  if (res.status >= 500) {
    errors500.add(1);
    unexpectedFailures.add(1);
    return;
  }
  if (res.status >= 400) {
    unexpectedFailures.add(1);
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
  if (loginRes.status !== 200) throw new Error('Login failed');
  return { token: loginRes.json('token') };
}

export default function (data) {
  const headers = { Authorization: `Bearer ${data.token}`, tags: { type: 'read' } };

  const endpoints = [
    `${BASE_URL}/api/dashboard/summary`,
    `${BASE_URL}/api/transactions?pageSize=50`,
    `${BASE_URL}/api/reports/open`,
    `${BASE_URL}/api/reports/department-incoming-closed`,
    `${BASE_URL}/api/reports/response-required`,
  ];

  for (const url of endpoints) {
    const res = http.get(url, headers);
    track(res);
    check(res, { [`${url} 200`]: (r) => r.status === 200 });
  }

  sleep(0.5);
}

export function handleSummary(data) {
  return { stdout: textSummary(data, { indent: ' ', enableColors: true }) };
}
