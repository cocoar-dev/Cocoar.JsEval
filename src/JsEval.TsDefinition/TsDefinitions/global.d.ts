declare function exit(): void;

declare function NewObject<T>(typeName: string, ...args: any[]): T;

declare function require(moduleName: string): any;

declare function fetch(url: string, options?: {
    method?: string;
    headers?: Record<string, string>;
    body?: string;
}): Promise<{
    ok: boolean;
    status: number;
    statusText: string;
    url: string;
    redirected: boolean;
    headers: Record<string, string>;
    text(): Promise<string>;
    json(): Promise<any>;
    arrayBuffer(): Promise<ArrayBuffer>;
}>;

declare var fetchOptions: {
    ignoreCertificateErrors(value: boolean): typeof fetchOptions;
    timeout(seconds: number): typeof fetchOptions;
    proxy(url: string): typeof fetchOptions;
} | undefined;
