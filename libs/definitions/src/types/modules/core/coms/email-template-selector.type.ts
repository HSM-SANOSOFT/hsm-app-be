export type EmailTemplateSelector<TData> = {
  subject?: string;
  template: (data: TData) => React.ReactNode;
};
