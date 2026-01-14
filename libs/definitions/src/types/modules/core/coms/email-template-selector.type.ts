export type EmailTemplateSelector<TData> = {
  title?: string;
  subject?: string;
  template: (data: TData) => React.ReactNode;
};
