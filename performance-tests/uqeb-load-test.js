import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Counter, Rate } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const USERNAME = __ENV.USERNAME || 'admin';
const PASSWORD = __ENV.PASSWORD || 'Admin@123';
const SCENARIO = (__ENV.K6_SCENARIO || 'smoke').toLowerCase();

const serverErrors = new Counter('server_errors_5xx');
const expectedClientErrors = new Counter('expected_client_errors_4xx');
const unexpectedFailures = new Rate('unexpected_failures');
let firstFailureBody = null;

const scenarioProfiles = {
  smoke: {
    executor: 'constant-vus',
    vus: 1,
    duration: '1m',
  },
  load: {
    executor: 'constant-vus',
    vus: 20,
    duration: '5m',
  },
  stress: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '1m', target: 50 },
      { duration: '3m', target: 100 },
      { duration: '1m', target: 0 },
    ],
  },
  spike: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '30s', target: 5 },
      { duration: '10s', target: 100 },
      { duration: '1m', target: 100 },
      { duration: '30s', target: 0 },
    ],
  },
};

const selected = scenarioProfiles[SCENARIO] || scenarioProfiles.smoke;

export const options = {
  scenarios: {
    main: {
      ...selected,
      tags: { test_type: SCENARIO },
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    unexpected_failures: ['rate<0.01'],
    'http_req_duration{type:read}': ['p(95)<1000'],
    'http_req_duration{type:write}': ['p(95)<2000'],
    server_errors_5xx: ['count==0'],
  },
};

function authHeaders(token) {
  return {
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    tags: { type: 'read' },
  };
}

function login() {
  const res = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ username: USERNAME, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' }, tags: { type: 'read' } }
  );
  trackResponse(res, true);
  const ok = check(res, { 'login status 200': (r) => r.status === 200 });
  if (!ok) return null;
  return res.json('token');
}

function trackResponse(res, allow4xx = false) {
  if (res.status >= 500) {
    if (!firstFailureBody) {
      firstFailureBody = res.body;
      console.error(`First 500 response (status ${res.status}): ${res.body}`);
    }
    serverErrors.add(1);
    unexpectedFailures.add(1);
    return;
  }
  if (res.status >= 400) {
    if (allow4xx) {
      expectedClientErrors.add(1);
      return;
    }
    unexpectedFailures.add(1);
    return;
  }
  unexpectedFailures.add(0);
}

function todayIso() {
  return new Date().toISOString().split('T')[0] + 'T00:00:00';
}

export default function () {
  const token = login();
  if (!token) return;

  const headers = authHeaders(token);

  group('read APIs', () => {
    const dashboard = http.get(`${BASE_URL}/api/dashboard/summary`, headers);
    trackResponse(dashboard);
    check(dashboard, { 'dashboard 200': (r) => r.status === 200 });

    const list = http.get(`${BASE_URL}/api/transactions?pageSize=20`, headers);
    trackResponse(list);
    check(list, { 'transactions 200': (r) => r.status === 200 });

    const report = http.get(`${BASE_URL}/api/reports/department-incoming-closed`, headers);
    trackResponse(report);
    check(report, { 'department report 200': (r) => r.status === 200 });
  });

  group('workflow', () => {
    const categories = http.get(`${BASE_URL}/api/categories`, headers);
    const parties = http.get(`${BASE_URL}/api/external-parties`, headers);
    const departments = http.get(`${BASE_URL}/api/departments`, headers);
    trackResponse(categories);
    trackResponse(parties);
    trackResponse(departments);

    const categoryId = categories.json()?.[0]?.id || 1;
    const partyId = parties.json()?.[0]?.id || 1;
    const departmentId = departments.json()?.[0]?.id || 1;

    const incomingNumber = `LOAD-TEST-${__VU}-${__ITER}-${Date.now()}`;
    const createPayload = JSON.stringify({
      incomingNumber,
      incomingDate: todayIso(),
      subject: `معاملة اختبار تحمل ${incomingNumber}`,
      incomingSourceType: 'External',
      incomingFromPartyId: partyId,
      incomingFromDepartmentId: null,
      responseType: 'External',
      responseDueDays: 7,
      priority: 'Normal',
      categoryId,
      notes: 'LOAD-TEST',
    });

    const createRes = http.post(
      `${BASE_URL}/api/transactions`,
      createPayload,
      { ...headers, tags: { type: 'write' } }
    );
    trackResponse(createRes);
    check(createRes, { 'create transaction 201': (r) => r.status === 201 });
    if (createRes.status !== 201) return;

    const txId = createRes.json('id');

    const assignPayload = JSON.stringify({
      departmentId,
      assignedDate: todayIso(),
      requiredAction: 'مراجعة اختبار التحمل',
      replyDueDays: 3,
    });
    const assignRes = http.post(
      `${BASE_URL}/api/transactions/${txId}/assignments`,
      assignPayload,
      { ...headers, tags: { type: 'write' } }
    );
    trackResponse(assignRes);
    check(assignRes, { 'add assignment 200': (r) => r.status === 200 });
    const assignmentId = assignRes.json('id');

    const followupPayload = JSON.stringify({
      followUpNumber: `FU-${incomingNumber}`,
      followUpDate: todayIso(),
      departmentIds: [departmentId],
      notes: 'تعقيب اختبار تحمل',
    });
    const followupRes = http.post(
      `${BASE_URL}/api/transactions/${txId}/followups`,
      followupPayload,
      { ...headers, tags: { type: 'write' } }
    );
    trackResponse(followupRes);
    check(followupRes, { 'add followup 200': (r) => r.status === 200 });

    const closePending = http.post(
      `${BASE_URL}/api/transactions/${txId}/close`,
      null,
      { ...headers, tags: { type: 'write' } }
    );
    trackResponse(closePending, true);
    check(closePending, { 'close with pending replies fails 400': (r) => r.status === 400 });

    if (assignmentId) {
      const replyPayload = JSON.stringify({
        replyDate: todayIso(),
        replySummary: 'رد اختبار التحمل',
      });
      const replyRes = http.post(
        `${BASE_URL}/api/transactions/${txId}/assignments/${assignmentId}/reply`,
        replyPayload,
        { ...headers, tags: { type: 'write' } }
      );
      trackResponse(replyRes);
      check(replyRes, { 'assignment reply 200': (r) => r.status === 200 });
    }

    const closeRes = http.post(
      `${BASE_URL}/api/transactions/${txId}/close`,
      null,
      { ...headers, tags: { type: 'write' } }
    );
    trackResponse(closeRes, true);
    check(closeRes, { 'close after reply succeeds or expected 400': (r) => r.status === 200 || r.status === 400 });
  });

  sleep(1);
}
