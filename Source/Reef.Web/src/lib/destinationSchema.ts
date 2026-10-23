export interface DestField {
  key: string;
  label: string;
  kind: 'text' | 'password' | 'number' | 'bool' | 'select' | 'textarea' | 'email' | 'url';
  secret?: boolean;
  default?: string | number | boolean;
  options?: [string, string][];
  placeholder?: string;
  hint?: string;
  advanced?: boolean;
  ui?: boolean;
  when?: (c: Record<string, any>) => boolean;
}

export const DESTINATION_TYPES: [string, string][] = [
  ['Local', 'File system'],
  ['Ftp', 'FTP or FTPS'],
  ['Sftp', 'SFTP'],
  ['AzureBlob', 'Azure Blob Storage'],
  ['S3', 'Amazon S3'],
  ['Email', 'Email'],
  ['Http', 'HTTP API'],
  ['NetworkShare', 'SMB network share'],
];

export const ENDPOINT_TYPES = ['Http', 'Sftp', 'Ftp', 'S3'];

const REGIONS: [string, string][] = [
  ['', 'Choose'],
  ['us-east-1', 'US East, N. Virginia'],
  ['us-east-2', 'US East, Ohio'],
  ['us-west-1', 'US West, N. California'],
  ['us-west-2', 'US West, Oregon'],
  ['eu-west-1', 'Europe, Ireland'],
  ['eu-west-2', 'Europe, London'],
  ['eu-west-3', 'Europe, Paris'],
  ['eu-central-1', 'Europe, Frankfurt'],
  ['ap-southeast-1', 'Asia Pacific, Singapore'],
  ['ap-southeast-2', 'Asia Pacific, Sydney'],
  ['ap-northeast-1', 'Asia Pacific, Tokyo'],
  ['ap-south-1', 'Asia Pacific, Mumbai'],
  ['sa-east-1', 'South America, São Paulo'],
  ['ca-central-1', 'Canada, Central'],
];

const smtp = (c: Record<string, any>) => (c.EmailProvider || 'Smtp') === 'Smtp';
const httpAuth =
  (...types: string[]) =>
  (c: Record<string, any>) =>
    types.includes(c.AuthType ?? '');

export const DESTINATION_FIELDS: Record<string, DestField[]> = {
  Local: [
    { key: 'BasePath', label: 'Base path', kind: 'text', placeholder: 'C:/Apps/reef/exports or ./exports' },
    { key: 'UseRelativePath', label: 'Resolve the path relative to the Reef folder', kind: 'bool' },
  ],
  Ftp: [
    { key: 'Host', label: 'Host', kind: 'text', placeholder: 'ftp.example.com' },
    { key: 'Port', label: 'Port', kind: 'number', default: 21 },
    { key: 'Username', label: 'Username', kind: 'text', secret: true },
    { key: 'Password', label: 'Password', kind: 'password', secret: true },
    { key: 'RemotePath', label: 'Remote path', kind: 'text', placeholder: '/uploads' },
    { key: 'UsePassiveMode', label: 'Passive mode', kind: 'bool', default: true, advanced: true },
    { key: 'UseSsl', label: 'SSL or TLS (FTPS)', kind: 'bool', advanced: true },
    { key: 'TimeoutSeconds', label: 'Timeout in seconds', kind: 'number', default: 60, advanced: true },
  ],
  Sftp: [
    { key: 'Host', label: 'Host', kind: 'text', placeholder: 'sftp.example.com' },
    { key: 'Port', label: 'Port', kind: 'number', default: 22 },
    { key: 'Username', label: 'Username', kind: 'text' },
    {
      key: '_auth',
      label: 'Authentication',
      kind: 'select',
      ui: true,
      default: 'password',
      options: [
        ['password', 'Password'],
        ['privatekey', 'Private key'],
      ],
    },
    { key: 'Password', label: 'Password', kind: 'password', secret: true, when: (c) => c._auth !== 'privatekey' },
    {
      key: 'PrivateKey',
      label: 'Private key in PEM format',
      kind: 'textarea',
      secret: true,
      when: (c) => c._auth === 'privatekey',
      placeholder: '-----BEGIN OPENSSH PRIVATE KEY-----',
    },
    {
      key: 'PrivateKeyPassphrase',
      label: 'Key passphrase',
      kind: 'password',
      secret: true,
      when: (c) => c._auth === 'privatekey',
      hint: 'Leave empty for keys without a passphrase',
    },
    { key: 'RemotePath', label: 'Remote path', kind: 'text', placeholder: '/uploads' },
    { key: 'TimeoutSeconds', label: 'Timeout in seconds', kind: 'number', default: 60, advanced: true },
  ],
  AzureBlob: [
    {
      key: 'ConnectionString',
      label: 'Connection string',
      kind: 'textarea',
      secret: true,
      placeholder: 'DefaultEndpointsProtocol=https;AccountName=...',
    },
    { key: 'ContainerName', label: 'Container', kind: 'text', placeholder: 'exports' },
    { key: 'Prefix', label: 'Prefix', kind: 'text', placeholder: 'data/' },
  ],
  S3: [
    { key: 'AccessKey', label: 'Access key', kind: 'text', secret: true, placeholder: 'AKIAIOSFODNN7EXAMPLE' },
    { key: 'SecretKey', label: 'Secret key', kind: 'password', secret: true },
    { key: 'Region', label: 'Region', kind: 'select', options: REGIONS },
    { key: 'BucketName', label: 'Bucket', kind: 'text', placeholder: 'my-export-bucket' },
    { key: 'Prefix', label: 'Path prefix', kind: 'text', placeholder: 'exports/' },
    {
      key: 'ServiceUrl',
      label: 'Custom endpoint URL',
      kind: 'url',
      advanced: true,
      placeholder: 'https://minio.example.com:9000',
      hint: 'For S3-compatible storage such as MinIO',
    },
    {
      key: 'Acl',
      label: 'Access control',
      kind: 'select',
      default: 'private',
      advanced: true,
      options: [
        ['private', 'Private'],
        ['public-read', 'Public read'],
        ['public-read-write', 'Public read and write'],
        ['authenticated-read', 'Authenticated read'],
      ],
    },
    {
      key: 'StorageClass',
      label: 'Storage class',
      kind: 'select',
      default: 'STANDARD',
      advanced: true,
      options: [
        ['STANDARD', 'Standard'],
        ['STANDARD_IA', 'Standard, infrequent access'],
        ['ONEZONE_IA', 'One Zone, infrequent access'],
        ['INTELLIGENT_TIERING', 'Intelligent-Tiering'],
        ['GLACIER', 'Glacier'],
        ['GLACIER_IR', 'Glacier Instant Retrieval'],
        ['DEEP_ARCHIVE', 'Glacier Deep Archive'],
      ],
    },
    { key: 'ServerSideEncryption', label: 'Server-side encryption', kind: 'bool', default: true, advanced: true },
    { key: 'TimeoutSeconds', label: 'Timeout in seconds', kind: 'number', default: 300, advanced: true },
  ],
  Email: [
    {
      key: 'EmailProvider',
      label: 'Provider',
      kind: 'select',
      default: 'Smtp',
      options: [
        ['Smtp', 'SMTP'],
        ['Resend', 'Resend'],
        ['SendGrid', 'SendGrid'],
      ],
    },
    { key: 'SmtpServer', label: 'SMTP host', kind: 'text', placeholder: 'smtp.example.com', when: smtp },
    { key: 'SmtpPort', label: 'SMTP port', kind: 'number', default: 587, when: smtp },
    {
      key: 'AuthType',
      label: 'Authentication',
      kind: 'select',
      default: 'Basic',
      when: smtp,
      options: [
        ['Basic', 'Username and password'],
        ['OAuth2', 'OAuth 2.0 access token'],
        ['None', 'None'],
      ],
    },
    { key: 'SmtpUsername', label: 'Username', kind: 'text', secret: true, when: (c) => smtp(c) && (c.AuthType ?? 'Basic') === 'Basic' },
    {
      key: 'SmtpPassword',
      label: 'Password or app password',
      kind: 'password',
      secret: true,
      when: (c) => smtp(c) && (c.AuthType ?? 'Basic') === 'Basic',
    },
    { key: 'OauthToken', label: 'OAuth 2.0 access token', kind: 'password', secret: true, when: (c) => smtp(c) && c.AuthType === 'OAuth2' },
    { key: 'OauthUsername', label: 'OAuth 2.0 username', kind: 'text', when: (c) => smtp(c) && c.AuthType === 'OAuth2' },
    {
      key: 'SecurityMode',
      label: 'Security',
      kind: 'select',
      default: 'StartTls',
      advanced: true,
      when: smtp,
      options: [
        ['StartTls', 'STARTTLS'],
        ['None', 'None'],
        ['Auto', 'Automatic'],
      ],
    },
    { key: 'ValidateCertificate', label: 'Validate the server certificate', kind: 'bool', default: true, advanced: true, when: smtp },
    { key: 'TimeoutSeconds', label: 'Timeout in seconds', kind: 'number', default: 30, advanced: true, when: smtp },
    { key: 'RetryCount', label: 'Retries', kind: 'number', default: 3, advanced: true, when: smtp },
    {
      key: 'ResendApiKey',
      label: 'Resend API key',
      kind: 'password',
      secret: true,
      placeholder: 're_xxxxxxxx',
      when: (c) => c.EmailProvider === 'Resend',
    },
    {
      key: 'SendGridApiKey',
      label: 'SendGrid API key',
      kind: 'password',
      secret: true,
      placeholder: 'SG.xxxxxxxx',
      when: (c) => c.EmailProvider === 'SendGrid',
    },
    { key: 'FromAddress', label: 'From address', kind: 'email', placeholder: 'noreply@example.com' },
    { key: 'FromName', label: 'From name', kind: 'text', placeholder: 'Reef' },
  ],
  Http: [
    { key: 'Url', label: 'URL', kind: 'url', placeholder: 'https://api.example.com/upload' },
    {
      key: 'UploadFormat',
      label: 'Upload format',
      kind: 'select',
      default: 'raw',
      options: [
        ['raw', 'Raw binary'],
        ['multipart', 'Multipart form data'],
        ['json', 'JSON payload'],
      ],
    },
    {
      key: 'AuthType',
      label: 'Authentication',
      kind: 'select',
      default: '',
      options: [
        ['', 'None'],
        ['bearer', 'Bearer token'],
        ['basic', 'Basic'],
        ['apikey', 'API key'],
        ['token', 'Token'],
      ],
    },
    { key: 'Username', label: 'Username', kind: 'text', when: httpAuth('basic') },
    { key: 'Password', label: 'Password', kind: 'password', secret: true, when: httpAuth('basic') },
    { key: 'ApiKeyHeader', label: 'API key header', kind: 'text', placeholder: 'X-API-Key', when: httpAuth('apikey') },
    { key: 'AuthToken', label: 'Token', kind: 'password', secret: true, when: httpAuth('bearer', 'token', 'apikey') },
    { key: 'Headers', label: 'Custom headers in JSON', kind: 'textarea', placeholder: '{ "X-Source": "reef" }' },
    { key: 'FileFieldName', label: 'File field name', kind: 'text', placeholder: 'file', when: (c) => c.UploadFormat === 'multipart' },
    { key: 'TimeoutSeconds', label: 'Timeout in seconds', kind: 'number', default: 300 },
  ],
  NetworkShare: [
    { key: 'BasePath', label: 'UNC path', kind: 'text', placeholder: '\\\\server\\share' },
    { key: 'SubFolder', label: 'Subfolder', kind: 'text', placeholder: 'exports/data' },
    { key: 'UseRelativePath', label: 'Resolve the subfolder relative to the share', kind: 'bool' },
    { key: 'Username', label: 'Username', kind: 'text', secret: true },
    { key: 'Password', label: 'Password', kind: 'password', secret: true },
    { key: 'Domain', label: 'Domain', kind: 'text', placeholder: 'WORKGROUP' },
  ],
};

export function defaultsFor(type: string): Record<string, any> {
  const config: Record<string, any> = {};
  for (const f of DESTINATION_FIELDS[type] ?? []) if (f.default !== undefined) config[f.key] = f.default;
  return config;
}

export function fromStored(type: string, json: string | null | undefined): Record<string, any> {
  let stored: Record<string, any> = {};
  try {
    stored = json ? JSON.parse(json) : {};
  } catch {}
  const config = { ...defaultsFor(type), ...stored };
  if (type === 'Email' && !stored.EmailProvider)
    config.EmailProvider = stored.ResendApiKey ? 'Resend' : stored.SendGridApiKey ? 'SendGrid' : 'Smtp';
  if (type === 'Sftp') config._auth = stored.PrivateKey ? 'privatekey' : 'password';
  if (config.Headers && typeof config.Headers === 'object') config.Headers = JSON.stringify(config.Headers, null, 2);
  return config;
}

export function toStored(type: string, config: Record<string, any>): string {
  const out: Record<string, any> = {};
  for (const f of DESTINATION_FIELDS[type] ?? []) {
    if (f.ui || (f.when && !f.when(config))) continue;
    const value = config[f.key];
    if (f.kind === 'bool') out[f.key] = value === true;
    else if (f.kind === 'number') out[f.key] = Number.parseInt(String(value ?? ''), 10) || 0;
    else if (value === undefined || value === null || value === '') {
      if (f.secret && config[`__secret_${f.key}`]) out[f.key] = '[SECRET]';
    } else if (f.key === 'Headers') {
      try {
        out[f.key] = JSON.parse(value);
      } catch {
        out[f.key] = value;
      }
    } else if (f.key.toLowerCase().includes('path') && typeof value === 'string') out[f.key] = value.replace(/[\\/]+$/, '');
    else out[f.key] = value;
  }
  return JSON.stringify(out);
}

export function maskSecrets(type: string, config: Record<string, any>): Record<string, any> {
  for (const f of DESTINATION_FIELDS[type] ?? []) {
    if (f.secret && config[f.key] === '[SECRET]') {
      config[f.key] = '';
      config[`__secret_${f.key}`] = true;
    }
  }
  return config;
}
