type HeadersMap = Record<string, string>;

type RequestConfig = {
  baseURL?: string;
  url?: string;
  method?: string;
  data?: unknown;
  params?: Record<string, unknown>;
  headers?: HeadersInit | HeadersMap;
  responseType?: 'json' | 'blob' | 'text';
  signal?: AbortSignal;
};

type ResponseShape<T = unknown> = {
  data: T;
  status: number;
  statusText: string;
  headers: HeadersMap;
  config: RequestConfig;
  request?: unknown;
};

type RequestInterceptor = (config: RequestConfig) => RequestConfig | Promise<RequestConfig>;
type ResponseSuccessInterceptor = <T>(response: ResponseShape<T>) => ResponseShape<T> | Promise<ResponseShape<T>>;
type ResponseErrorInterceptor = (error: unknown) => unknown;

export class AxiosHeaders {
  private values: HeadersMap = {};

  constructor(initial?: HeadersInit | HeadersMap) {
    if (initial) {
      this.merge(initial);
    }
  }

  set(name: string, value: string): void {
    this.values[name.toLowerCase()] = value;
  }

  get(name: string): string | undefined {
    return this.values[name.toLowerCase()];
  }

  toJSON(): HeadersMap {
    return { ...this.values };
  }

  private merge(initial: HeadersInit | HeadersMap): void {
    new Headers(initial).forEach((value, key) => {
      this.values[key.toLowerCase()] = value;
    });
  }
}

export class AxiosError<T = unknown> extends Error {
  isAxiosError = true;
  code?: string;
  config?: RequestConfig;
  request?: unknown;
  response?: ResponseShape<T>;
  status?: number;

  constructor(
    message: string,
    code?: string,
    config?: RequestConfig,
    request?: unknown,
    response?: ResponseShape<T>,
  ) {
    super(message);
    this.name = 'AxiosError';
    this.code = code;
    this.config = config;
    this.request = request;
    this.response = response;
    this.status = response?.status;
  }
}

function isAxiosError(error: unknown): error is AxiosError {
  return Boolean(error && typeof error === 'object' && (error as { isAxiosError?: unknown }).isAxiosError === true);
}

function normalizeHeaders(headers?: HeadersInit | HeadersMap): HeadersMap {
  const normalized: HeadersMap = {};
  if (!headers) return normalized;

  new Headers(headers).forEach((value, key) => {
    normalized[key.toLowerCase()] = value;
  });

  return normalized;
}

function mergeHeaders(...headers: Array<HeadersInit | HeadersMap | undefined>): HeadersMap {
  return headers.reduce<HeadersMap>((merged, current) => ({
    ...merged,
    ...normalizeHeaders(current),
  }), {});
}

function appendParams(url: string, params?: Record<string, unknown>): string {
  if (!params) return url;

  const searchParams = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === null || value === undefined) return;
    if (Array.isArray(value)) {
      value.forEach((item) => {
        if (item !== null && item !== undefined) searchParams.append(key, String(item));
      });
      return;
    }
    searchParams.append(key, String(value));
  });

  const query = searchParams.toString();
  if (!query) return url;
  return `${url}${url.includes('?') ? '&' : '?'}${query}`;
}

function buildUrl(baseURL: string | undefined, url: string, params?: Record<string, unknown>): string {
  const combined = /^https?:\/\//i.test(url)
    ? url
    : `${(baseURL || '').replace(/\/+$/, '')}/${url.replace(/^\/+/, '')}`;

  return appendParams(combined, params);
}

function readResponseHeaders(headers: Headers): HeadersMap {
  const result: HeadersMap = {};
  headers.forEach((value, key) => {
    result[key.toLowerCase()] = value;
  });
  return result;
}

async function readResponseData<T>(response: Response, responseType?: RequestConfig['responseType']): Promise<T> {
  if (response.status === 204) return null as T;
  if (responseType === 'blob') return response.blob() as Promise<T>;
  if (responseType === 'text') return response.text() as Promise<T>;

  const text = await response.text();
  if (!text) return null as T;

  const contentType = response.headers.get('content-type') ?? '';
  return (contentType.includes('application/json') ? JSON.parse(text) : text) as T;
}

function create(initialConfig: RequestConfig = {}) {
  const requestInterceptors: RequestInterceptor[] = [];
  const responseSuccessInterceptors: ResponseSuccessInterceptor[] = [];
  const responseErrorInterceptors: ResponseErrorInterceptor[] = [];

  async function applyResponseError(error: unknown): Promise<never> {
    let nextError = error;
    for (const interceptor of responseErrorInterceptors) {
      try {
        const value = interceptor(nextError);
        nextError = value instanceof Promise ? await value : value;
      } catch (interceptorError) {
        nextError = interceptorError;
      }
    }
    throw nextError;
  }

  async function request<T>(config: RequestConfig): Promise<ResponseShape<T>> {
    let nextConfig: RequestConfig = {
      ...initialConfig,
      ...config,
      headers: mergeHeaders(initialConfig.headers, config.headers),
    };

    for (const interceptor of requestInterceptors) {
      nextConfig = await interceptor(nextConfig);
    }

    const method = (nextConfig.method ?? 'GET').toUpperCase();
    const headers = mergeHeaders(nextConfig.headers);
    const isFormData = nextConfig.data instanceof FormData;
    const body: BodyInit | undefined = method === 'GET' || method === 'HEAD'
      ? undefined
      : isFormData
        ? nextConfig.data as FormData
        : nextConfig.data === undefined
          ? undefined
          : JSON.stringify(nextConfig.data);

    if (isFormData) {
      delete headers['content-type'];
    }

    try {
      const fetchResponse = await fetch(buildUrl(nextConfig.baseURL, nextConfig.url ?? '', nextConfig.params), {
        method,
        headers,
        body,
        signal: nextConfig.signal,
      });

      const response: ResponseShape<T> = {
        data: await readResponseData<T>(fetchResponse, nextConfig.responseType),
        status: fetchResponse.status,
        statusText: fetchResponse.statusText,
        headers: readResponseHeaders(fetchResponse.headers),
        config: nextConfig,
      };

      if (!fetchResponse.ok) {
        throw new AxiosError(
          `Request failed with status code ${fetchResponse.status}`,
          fetchResponse.status >= 500 ? 'ERR_BAD_RESPONSE' : 'ERR_BAD_REQUEST',
          nextConfig,
          undefined,
          response,
        );
      }

      let nextResponse = response;
      for (const interceptor of responseSuccessInterceptors) {
        nextResponse = await interceptor(nextResponse);
      }
      return nextResponse;
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return applyResponseError(new AxiosError('canceled', 'ERR_CANCELED', nextConfig));
      }

      if (isAxiosError(error)) {
        return applyResponseError(error);
      }

      return applyResponseError(new AxiosError(
        error instanceof Error ? error.message : 'Network Error',
        'ERR_NETWORK',
        nextConfig,
      ));
    }
  }

  return {
    interceptors: {
      request: {
        use(interceptor: RequestInterceptor) {
          requestInterceptors.push(interceptor);
          return requestInterceptors.length - 1;
        },
      },
      response: {
        use(success: ResponseSuccessInterceptor, error?: ResponseErrorInterceptor) {
          responseSuccessInterceptors.push(success);
          if (error) responseErrorInterceptors.push(error);
          return responseSuccessInterceptors.length - 1;
        },
      },
    },
    get: <T>(url: string, config?: RequestConfig) => request<T>({ ...config, url, method: 'GET' }),
    delete: <T>(url: string, config?: RequestConfig) => request<T>({ ...config, url, method: 'DELETE' }),
    post: <T>(url: string, data?: unknown, config?: RequestConfig) => request<T>({ ...config, url, data, method: 'POST' }),
    put: <T>(url: string, data?: unknown, config?: RequestConfig) => request<T>({ ...config, url, data, method: 'PUT' }),
    patch: <T>(url: string, data?: unknown, config?: RequestConfig) => request<T>({ ...config, url, data, method: 'PATCH' }),
  };
}

const axios = {
  AxiosError,
  AxiosHeaders,
  create,
  isAxiosError,
};

export { create, isAxiosError };
export default axios;
