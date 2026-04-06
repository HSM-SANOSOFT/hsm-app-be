import { FacTemplateDtoMap } from
'@hsm-lib/common/
import { FacEmailTemplate } from
'@hsm-lib/common/
import { EmailRegistryFromDtoMap } from
'@hsm-lib/common/

import { FacturaGeneradaTemplate } from './factura-generada.template';
import { FacturaListaTemplate } from './factura-lista.template';

export const facEmailRegistry = {
  [FacEmailTemplate.FacturaGenerada]: {
    title: 'Factura Generada',
    subject: 'Su factura ha sido generada',
    template: FacturaGeneradaTemplate,
  },
  [FacEmailTemplate.FacturaLista]: {
    title: 'Factura Lista',
    subject: 'Su factura está lista para descargar',
    template: FacturaListaTemplate,
  },
} satisfies EmailRegistryFromDtoMap<typeof FacTemplateDtoMap>;
