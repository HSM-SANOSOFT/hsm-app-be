import { AuthEmailTemplate } from '@hsm-lib/definitions/enums';
import { AuthEmailRegistry } from '@hsm-lib/definitions/types';

export const authEmailRegistry: AuthEmailRegistry = {
  [AuthEmailTemplate.PinInicioSesion]: {
    title: 'PIN de Inicio de Sesión',
    subject: 'Tu PIN de Inicio de Sesión',
    template: () => null,
  },
  [AuthEmailTemplate.PinRegistro]: {
    title: 'PIN de Registro',
    subject: 'Tu PIN de Registro',
    template: () => null,
  },
  [AuthEmailTemplate.PinRestablecerContrasena]: {
    title: 'PIN de Restablecimiento de Contraseña',
    subject: 'Tu PIN de Restablecimiento de Contraseña',
    template: () => null,
  },
  [AuthEmailTemplate.PinResultadoExamen]: {
    title: 'PIN para Resultados de Exámenes',
    subject: 'Tu PIN para Resultados de Exámenes',
    template: () => null,
  },
} satisfies AuthEmailRegistry;
