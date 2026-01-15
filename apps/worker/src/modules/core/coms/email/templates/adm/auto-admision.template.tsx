import { AutoAdmisionTemplateDto } from '@hsm-lib/definitions/dtos';
export function AutoAdmisionTemplate(
  data: AutoAdmisionTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: {data.patientName}
        <br />
        <br />
        Le saludamos del Hospital Santamaría.
        <br />
        Hemos registrado a su historia clínica #{data.documentId}, un acceso con
        fecha {data.date}. Que contiene un código de verificación necesario
        identificarse en nuestro sistema.
        <br />
        <br />
        Código de verificación: {data.code}
        <br />
        <br />
        Gracias por su colaboración.
        <br />
        Atentamente,
        <br />
        Admisiones
        <br />
        Servicio Ambulatorio
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
